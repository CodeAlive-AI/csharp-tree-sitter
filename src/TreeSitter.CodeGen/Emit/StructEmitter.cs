using TreeSitter.CodeGen.Model;
using TreeSitter.CodeGen.Naming;
using TreeSitter.CodeGen.Schema;

namespace TreeSitter.CodeGen.Emit;

/// <summary>
/// Emits the body of a single typed struct (concrete node, supertype, union, or
/// unnamed token) into a shared <see cref="CodeWriter"/>. One instance per grammar.
/// </summary>
internal sealed class StructEmitter
{
    private readonly CodeWriter _w;
    private readonly TypeRegistry _registry;

    public StructEmitter(CodeWriter writer, TypeRegistry registry)
    {
        _w = writer;
        _registry = registry;
    }

    /// <summary>Emits a leaf typed struct that has no accessors (unnamed token or childless named node).</summary>
    public void EmitLeaf(TypeEntry entry, NodeType nt)
    {
        EmitDocSummary($"The <c>{Escape(nt.Type)}</c> {(nt.Named ? "node" : "token")}.");
        _w.OpenBrace($"public readonly partial struct {entry.TypeName} : global::TreeSitter.Typed.ITypedNode<{entry.TypeName}>");
        EmitCommonMembers(entry, nt.Type);
        _w.CloseBrace();
    }

    /// <summary>Emits a concrete named node struct with its field and children accessors.</summary>
    public void EmitConcrete(TypeEntry entry, NodeType nt)
    {
        EmitDocSummary($"The <c>{Escape(nt.Type)}</c> node.");
        _w.OpenBrace($"public readonly partial struct {entry.TypeName} : global::TreeSitter.Typed.ITypedNode<{entry.TypeName}>");
        EmitCommonMembers(entry, nt.Type);

        // Per-struct member-name scope: Kind/Node/Accepts/TryFrom/FromUnchecked are taken.
        var members = new NameAllocator(["Kind", "Node", "Accepts", "TryFrom", "FromUnchecked", entry.TypeName]);

        // Fields (deterministic ordinal order by field name).
        foreach (KeyValuePair<string, ChildSet> field in nt.Fields.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            string memberBase = Identifiers.ToMemberName(field.Key);
            string member = members.Allocate(memberBase);
            EmitFieldAccessor(member, field.Key, field.Value);
        }

        // Unnamed children group.
        if (nt.Children is { } children && children.Types.Count > 0)
        {
            string memberBase = ChildrenMemberBaseName(children);
            string member = members.Allocate(memberBase);
            EmitChildrenAccessor(member, children);
        }

        _w.CloseBrace();
    }

    /// <summary>Emits a supertype struct: variant downcasts, a discriminator, and matchers.</summary>
    public void EmitSupertype(TypeEntry entry, NodeType nt)
    {
        IReadOnlyList<TypeRef> subtypes = nt.Subtypes!;
        EmitDocSummary(
            $"The <c>{Escape(nt.Type)}</c> supertype: any of its subtypes. Variants that " +
            "are themselves supertypes are exposed transitively (their own subtypes are " +
            "included in the accepted kinds).");
        _w.OpenBrace($"public readonly partial struct {entry.TypeName} : global::TreeSitter.Typed.ITypedNode<{entry.TypeName}>");
        EmitCommonMembers(entry, nt.Type);

        // Direct variants, deduplicated by full name, in deterministic order.
        IReadOnlyList<VariantInfo> variants = BuildVariants(subtypes);
        EmitVariantApi(entry, variants);

        _w.CloseBrace();
    }

    /// <summary>Emits an anonymous-union struct over its member types.</summary>
    public void EmitUnion(AnonUnion union)
    {
        EmitDocSummary("An anonymous union generated for a multi-type field or children group.");
        _w.OpenBrace($"public readonly partial struct {union.TypeName} : global::TreeSitter.Typed.ITypedNode<{union.TypeName}>");

        // Accepts for a union = union of members' accepted kinds.
        EmitUnionCommonMembers(union);

        var variants = union.Members.Select(m => new VariantInfo(m, m.FullName)).ToList();
        EmitVariantApi(NewSyntheticEntry(union), variants);

        _w.CloseBrace();
    }

    // ---- shared member emission ------------------------------------------------

    private void EmitCommonMembers(TypeEntry entry, string sexp)
    {
        IReadOnlyList<string> leafKinds = _registry.FlattenToLeafKinds(sexp, entry.Named);
        bool single = leafKinds.Count == 1 && leafKinds[0] == sexp;

        _w.Blank();
        _w.Line($"/// <summary>The s-expression kind string for this node type.</summary>");
        _w.Line($"public const string Kind = {Literal(sexp)};");
        _w.Blank();

        _w.Line("/// <summary>The underlying untyped node.</summary>");
        _w.Line("public global::TreeSitter.Node Node { get; }");
        _w.Blank();

        _w.Line($"/// <summary>Wraps <paramref name=\"node\"/> as a <see cref=\"{entry.TypeName}\"/> without any kind check.</summary>");
        _w.Line("/// <param name=\"node\">A node whose kind is already known to be accepted.</param>");
        _w.OpenBrace($"private {entry.TypeName}(global::TreeSitter.Node node)");
        _w.Line("Node = node;");
        _w.CloseBrace();
        _w.Blank();

        _w.Line("/// <summary>Determines whether a node of the given kind is accepted by this type.</summary>");
        _w.Line("/// <param name=\"kind\">A node kind string.</param>");
        if (single)
        {
            _w.Line("public static bool Accepts(string kind) => kind == Kind;");
        }
        else
        {
            _w.OpenBrace("public static bool Accepts(string kind) => kind switch");
            foreach (string k in leafKinds)
                _w.Line($"{Literal(k)} => true,");
            _w.Line("_ => false,");
            _w.Outdent();
            _w.Line("};");
        }
        _w.Blank();

        _w.Line("/// <summary>Wraps the node if its kind is accepted, otherwise returns <see langword=\"null\"/>.</summary>");
        _w.Line("/// <param name=\"node\">The node to wrap.</param>");
        _w.Line($"public static {entry.TypeName}? TryFrom(global::TreeSitter.Node node) => !node.IsNull && Accepts(node.Kind) ? new {entry.TypeName}(node) : null;");
        _w.Blank();

        _w.Line("/// <summary>Wraps the node without validation; a debug assert guards the kind. Use only when the kind is already known.</summary>");
        _w.Line("/// <param name=\"node\">An already-validated node.</param>");
        _w.OpenBrace($"public static {entry.TypeName} FromUnchecked(global::TreeSitter.Node node)");
        _w.Line($"global::System.Diagnostics.Debug.Assert(Accepts(node.Kind), \"Node kind '\" + node.Kind + \"' is not valid for {entry.TypeName}.\");");
        _w.Line("return new(node);");
        _w.CloseBrace();
    }

    private void EmitUnionCommonMembers(AnonUnion union)
    {
        // Gather all leaf kinds across all members.
        var leafKinds = new SortedSet<string>(StringComparer.Ordinal);
        foreach (TypeEntry m in union.Members)
            foreach (string k in _registry.FlattenToLeafKinds(m.SExpression, m.Named))
                leafKinds.Add(k);

        _w.Blank();
        _w.Line("/// <summary>The underlying untyped node.</summary>");
        _w.Line("public global::TreeSitter.Node Node { get; }");
        _w.Blank();

        _w.Line($"/// <summary>Wraps <paramref name=\"node\"/> as a <see cref=\"{union.TypeName}\"/> without any kind check.</summary>");
        _w.Line("/// <param name=\"node\">A node whose kind is already known to be accepted.</param>");
        _w.OpenBrace($"private {union.TypeName}(global::TreeSitter.Node node)");
        _w.Line("Node = node;");
        _w.CloseBrace();
        _w.Blank();

        _w.Line("/// <summary>Determines whether a node of the given kind is accepted by this union.</summary>");
        _w.Line("/// <param name=\"kind\">A node kind string.</param>");
        _w.OpenBrace("public static bool Accepts(string kind) => kind switch");
        foreach (string k in leafKinds)
            _w.Line($"{Literal(k)} => true,");
        _w.Line("_ => false,");
        _w.Outdent();
        _w.Line("};");
        _w.Blank();

        _w.Line("/// <summary>Wraps the node if its kind is accepted, otherwise returns <see langword=\"null\"/>.</summary>");
        _w.Line("/// <param name=\"node\">The node to wrap.</param>");
        _w.Line($"public static {union.TypeName}? TryFrom(global::TreeSitter.Node node) => !node.IsNull && Accepts(node.Kind) ? new {union.TypeName}(node) : null;");
        _w.Blank();

        _w.Line("/// <summary>Wraps the node without validation; a debug assert guards the kind. Use only when the kind is already known.</summary>");
        _w.Line("/// <param name=\"node\">An already-validated node.</param>");
        _w.OpenBrace($"public static {union.TypeName} FromUnchecked(global::TreeSitter.Node node)");
        _w.Line($"global::System.Diagnostics.Debug.Assert(Accepts(node.Kind), \"Node kind '\" + node.Kind + \"' is not valid for {union.TypeName}.\");");
        _w.Line("return new(node);");
        _w.CloseBrace();
    }

    // ---- field / children accessors -------------------------------------------

    private void EmitFieldAccessor(string member, string fieldName, ChildSet field)
    {
        ResolvedMemberType resolved = _registry.ResolveMemberType(field.Types);
        string t = resolved.FullName;

        if (field.Multiple)
        {
            EmitDocSummary($"The <c>{Escape(fieldName)}</c> field (zero or more), with extras filtered out.");
            _w.OpenBrace($"public global::System.Collections.Generic.IEnumerable<{t}> {member}");
            _w.OpenBrace("get");
            _w.OpenBrace($"foreach (global::TreeSitter.Node child in global::TreeSitter.Typed.TypedNodeExtensions.ChildrenByFieldName(Node, {Literal(fieldName)}))");
            _w.Line("if (child.IsExtra) continue;");
            _w.Line($"if ({t}.TryFrom(child) is {{ }} typed) yield return typed;");
            _w.CloseBrace();
            _w.CloseBrace();
            _w.CloseBrace();
        }
        else if (field.Required)
        {
            EmitDocSummary($"The required <c>{Escape(fieldName)}</c> field.");
            _w.Line($"/// <exception cref=\"global::TreeSitter.Typed.IncorrectNodeKindException\">The field is absent or has an unexpected kind.</exception>");
            // The exception's 3rd ctor arg is `acceptedKinds`, NOT the field name: passing
            // the field name there is misleading. Omit it (consistent with the required-child
            // accessor below); the field is named in the doc comment / call site.
            _w.Line($"public {t} {member} => {t}.TryFrom(Node.ChildByFieldName({Literal(fieldName)})) ?? throw new global::TreeSitter.Typed.IncorrectNodeKindException(Node, {Literal(t)});");
        }
        else
        {
            EmitDocSummary($"The optional <c>{Escape(fieldName)}</c> field, or <see langword=\"null\"/> when absent.");
            _w.Line($"public {t}? {member} => {t}.TryFrom(Node.ChildByFieldName({Literal(fieldName)}));");
        }
        _w.Blank();
    }

    private void EmitChildrenAccessor(string member, ChildSet children)
    {
        ResolvedMemberType resolved = _registry.ResolveMemberType(children.Types);
        string t = resolved.FullName;

        if (children.Multiple)
        {
            EmitDocSummary("The unnamed children (zero or more), with extras filtered out.");
            _w.OpenBrace($"public global::System.Collections.Generic.IEnumerable<{t}> {member}");
            _w.OpenBrace("get");
            _w.OpenBrace("foreach (global::TreeSitter.Node child in global::TreeSitter.Typed.TypedNodeExtensions.FieldlessChildren(Node))");
            _w.Line($"if ({t}.TryFrom(child) is {{ }} typed) yield return typed;");
            _w.CloseBrace();
            _w.CloseBrace();
            _w.CloseBrace();
        }
        else if (children.Required)
        {
            EmitDocSummary("The single unnamed child.");
            _w.Line($"/// <exception cref=\"global::TreeSitter.Typed.IncorrectNodeKindException\">The child is absent or has an unexpected kind.</exception>");
            _w.OpenBrace($"public {t} {member}");
            _w.OpenBrace("get");
            _w.Line("foreach (global::TreeSitter.Node child in global::TreeSitter.Typed.TypedNodeExtensions.FieldlessChildren(Node))");
            _w.Line($"    if ({t}.TryFrom(child) is {{ }} typed) return typed;");
            _w.Line($"throw new global::TreeSitter.Typed.IncorrectNodeKindException(Node, {Literal(t)});");
            _w.CloseBrace();
            _w.CloseBrace();
        }
        else
        {
            EmitDocSummary("The single unnamed child, or <see langword=\"null\"/> when absent.");
            _w.OpenBrace($"public {t}? {member}");
            _w.OpenBrace("get");
            _w.Line("foreach (global::TreeSitter.Node child in global::TreeSitter.Typed.TypedNodeExtensions.FieldlessChildren(Node))");
            _w.Line($"    if ({t}.TryFrom(child) is {{ }} typed) return typed;");
            _w.Line("return null;");
            _w.CloseBrace();
            _w.CloseBrace();
        }
        _w.Blank();
    }

    // ---- variant API (shared by supertypes and unions) ------------------------

    private void EmitVariantApi(TypeEntry entry, IReadOnlyList<VariantInfo> variants)
    {
        // Member names for variant accessors must be unique within the struct.
        var members = new NameAllocator(["Kind", "Node", "Accepts", "TryFrom", "FromUnchecked", "Which", "Match", "Switch", entry.TypeName]);
        var named = new List<(VariantInfo v, string accessor, string enumName)>();
        foreach (VariantInfo v in variants)
        {
            string simple = v.Member.TypeName;
            string accessor = members.Allocate("As" + simple);
            string enumName = simple; // enum member; collisions resolved below
            named.Add((v, accessor, enumName));
        }

        // Discriminator enum (deterministic order, unique members + Unknown).
        var enumScope = new NameAllocator(["Unknown"]);
        var enumNames = new List<(VariantInfo v, string accessor, string enumMember)>();
        foreach ((VariantInfo v, string accessor, string _) in named)
            enumNames.Add((v, accessor, enumScope.Allocate(v.Member.TypeName)));

        _w.Blank();
        _w.Line("/// <summary>Discriminates which variant the wrapped node is.</summary>");
        _w.OpenBrace($"public enum Variant");
        _w.Line("/// <summary>The node matched no known variant.</summary>");
        _w.Line("Unknown,");
        foreach ((VariantInfo v, string _, string enumMember) in enumNames)
        {
            _w.Line($"/// <summary>The <c>{Escape(v.Member.SExpression)}</c> variant.</summary>");
            _w.Line($"{enumMember},");
        }
        _w.CloseBrace();
        _w.Blank();

        // Which property. Variants of a supertype can overlap (a subtype that is
        // itself a supertype may share leaf kinds with a sibling, e.g. C's
        // _declarator / _field_declarator / _type_declarator). Emit each kind string
        // at most once; the first variant in deterministic order wins, mirroring how
        // a hand-written kind switch would be read top-to-bottom.
        _w.Line("/// <summary>The variant of the wrapped node. When variants overlap, the first declared variant that accepts the kind is reported.</summary>");
        _w.OpenBrace("public Variant Which => Node.Kind switch");
        var seenKinds = new HashSet<string>(StringComparer.Ordinal);
        foreach ((VariantInfo v, string _, string enumMember) in enumNames)
        {
            foreach (string k in _registry.FlattenToLeafKinds(v.Member.SExpression, v.Member.Named))
            {
                if (seenKinds.Add(k))
                    _w.Line($"{Literal(k)} => Variant.{enumMember},");
            }
        }
        _w.Line("_ => Variant.Unknown,");
        _w.Outdent();
        _w.Line("};");
        _w.Blank();

        // As<Variant>() accessors.
        foreach ((VariantInfo v, string accessor, string _) in enumNames)
        {
            _w.Line($"/// <summary>The node as <see cref=\"{v.Member.FullName}\"/>, or <see langword=\"null\"/> if it is a different variant.</summary>");
            _w.Line($"public {v.Member.FullName}? {accessor}() => {v.Member.FullName}.TryFrom(Node);");
            _w.Blank();
        }

        EmitMatchAndSwitch(entry.TypeName, enumNames);
    }

    private void EmitMatchAndSwitch(string enclosingTypeName, List<(VariantInfo v, string accessor, string enumMember)> variants)
    {
        // Parameter names: on{Variant}, unique.
        var paramScope = new NameAllocator();
        var paramNames = variants
            .Select(t => (t.v, param: paramScope.Allocate("on" + t.v.Member.TypeName)))
            .ToList();

        // Match<TResult>
        _w.Line("/// <summary>Exhaustively maps over the variants, returning a value.</summary>");
        string matchParams = string.Join(", ",
            paramNames.Select(t => $"global::System.Func<{t.v.Member.FullName}, TResult> {t.param}"));
        _w.OpenBrace($"public TResult Match<TResult>({matchParams})");
        _w.OpenBrace("return Which switch");
        for (int i = 0; i < variants.Count; i++)
        {
            (VariantInfo v, string _, string enumMember) = variants[i];
            string param = paramNames[i].param;
            _w.Line($"Variant.{enumMember} => {param}({v.Member.FullName}.FromUnchecked(Node)),");
        }
        _w.Line($"_ => throw new global::TreeSitter.Typed.IncorrectNodeKindException(Node, {Literal(enclosingTypeName)}),");
        _w.Outdent();
        _w.Line("};");
        _w.CloseBrace();
        _w.Blank();

        // Switch (Action-based)
        _w.Line("/// <summary>Exhaustively dispatches over the variants, performing an action.</summary>");
        string switchParams = string.Join(", ",
            paramNames.Select(t => $"global::System.Action<{t.v.Member.FullName}> {t.param}"));
        _w.OpenBrace($"public void Switch({switchParams})");
        _w.OpenBrace("switch (Which)");
        for (int i = 0; i < variants.Count; i++)
        {
            (VariantInfo v, string _, string enumMember) = variants[i];
            string param = paramNames[i].param;
            _w.Line($"case Variant.{enumMember}: {param}({v.Member.FullName}.FromUnchecked(Node)); break;");
        }
        _w.Line($"default: throw new global::TreeSitter.Typed.IncorrectNodeKindException(Node, {Literal(enclosingTypeName)});");
        _w.CloseBrace();
        _w.CloseBrace();
        _w.Blank();
    }

    // ---- helpers ---------------------------------------------------------------

    private IReadOnlyList<VariantInfo> BuildVariants(IReadOnlyList<TypeRef> subtypes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<VariantInfo>();
        foreach (TypeRef sub in subtypes.OrderBy(s => s.Type, StringComparer.Ordinal).ThenByDescending(s => s.Named))
        {
            TypeEntry e = _registry.Resolve(sub);
            if (seen.Add(e.FullName))
                list.Add(new VariantInfo(e, e.FullName));
        }
        return list;
    }

    private static string ChildrenMemberBaseName(ChildSet children)
    {
        if (children.Types.Count == 1)
        {
            string baseName = Identifiers.ToTypeName(children.Types[0].Type);
            return children.Multiple ? Pluralize(baseName) : baseName;
        }
        return children.Multiple ? "Children" : "Child";
    }

    private static string Pluralize(string name)
    {
        if (name.EndsWith('s') || name.EndsWith('x') || name.EndsWith('z'))
            return name + "es";
        if (name.EndsWith('y') && name.Length > 1 && !"aeiou".Contains(char.ToLowerInvariant(name[^2])))
            return name[..^1] + "ies";
        return name + "s";
    }

    private static TypeEntry NewSyntheticEntry(AnonUnion union) => new()
    {
        SExpression = union.Signature,
        Named = true,
        TypeName = union.TypeName,
        Category = TypeCategory.AnonUnion,
        FullName = union.FullName,
    };

    private void EmitDocSummary(string text) => _w.Line($"/// <summary>{text}</summary>");

    /// <summary>
    /// Escapes a node-type string for safe inclusion in an XML doc comment: XML
    /// metacharacters become entities and control characters (newlines, tabs, etc.)
    /// are rendered as visible C-style escapes so the comment stays on one valid line.
    /// </summary>
    private static string Escape(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (char.IsControl(c))
                        sb.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private static string Literal(string s) => Literals.String(s);
}

/// <summary>A direct variant of a supertype or union and its referenceable type.</summary>
internal readonly record struct VariantInfo(TypeEntry Member, string FullName);
