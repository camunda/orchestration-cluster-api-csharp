using System.Globalization;
using System.Text;

namespace Camunda.Orchestration.Sdk.Generator;

/// <summary>
/// Spec-content-safe emission helpers.
///
/// Every spec-derived string that lands in a generated <c>.cs</c> file MUST flow
/// through one of the helpers in this class:
///
/// <list type="bullet">
///   <item><description><see cref="SafeXmlDocText"/> — text inside <c>///</c> XML
///   doc comments. Escapes XML metacharacters AND replaces Unicode characters
///   that the C# lexer treats as line-terminators (U+2028, U+2029, U+0085) or
///   that are invisible/bidi (zero-width, override). Without this, a description
///   string can terminate the doc comment mid-line and inject executable
///   C# source on the next line.</description></item>
///   <item><description><see cref="SafeCSharpIdentifier"/> — bare C# identifiers
///   (type, member, parameter names). Normalizes to NFKC, restricts to the
///   character categories permitted by ECMA-334 §6.4.2, prefixes <c>@</c> for
///   reserved keywords, and rejects zero-width / bidi characters that produce
///   visually-distinct-but-textually-identical "Trojan Source" identifiers
///   (CVE-2021-42574).</description></item>
///   <item><description><see cref="SafeCSharpStringLiteral"/> — content of a
///   regular <c>"..."</c> string literal (e.g. a <c>JsonPropertyName</c>
///   attribute argument). Escapes <c>\</c>, <c>"</c>, and any Unicode
///   line-terminator that would otherwise break a regular string literal.
///   </description></item>
/// </list>
///
/// <see cref="ScanGeneratedSource"/> is the post-emit fail-fast backstop: any
/// generated <c>.cs</c> file containing forbidden code points anywhere is
/// rejected with the file path and offending offset, so a missed call site
/// becomes a loud build failure rather than a silent injection.
/// </summary>
internal static class SafeEmit
{
    // Code points that the C# lexical specification (ECMA-334 §6.3.3) treats as
    // new-line characters. Inside a `///` single-line comment, ANY of these
    // terminates the comment and starts a new C# logical line. Inside a regular
    // string literal `"..."` they are also forbidden and would break compilation.
    //
    // The HTML/XML decoded form of these is what's dangerous; emitting them as
    // numeric character references (`&#xNNNN;`) in a comment is safe because the
    // C# lexer never decodes XML entities.
    private static readonly char[] LineTerminators =
    [
        '\u2028', // LINE SEPARATOR
        '\u2029', // PARAGRAPH SEPARATOR
        '\u0085', // NEXT LINE
    ];

    // Invisible / bidi-control characters. Required to defend against the
    // "Trojan Source" class of attack (CVE-2021-42574): two strings that look
    // identical but compile to distinct identifiers (e.g. shadowing).
    // U+FEFF is also rejected to avoid stray BOMs.
    private static bool IsInvisibleOrBidi(int cp) =>
        cp is (>= 0x200B and <= 0x200F) // ZWSP, ZWNJ, ZWJ, LRM, RLM
            or (>= 0x202A and <= 0x202E) // LRE, RLE, PDF, LRO, RLO
            or (>= 0x2060 and <= 0x2064) // WJ, function application, invisible times/separator/plus
            or 0xFEFF;                   // ZWNBSP / BOM

    /// <summary>
    /// Sanitize a spec-derived string for emission inside an XML doc comment
    /// (<c>///</c> line). XML-escapes the five XML metacharacters AND replaces:
    /// <list type="bullet">
    ///   <item><description>Unicode line terminators
    ///   (U+2028, U+2029, U+0085) — these would terminate the
    ///   <c>///</c> comment per ECMA-334 §6.3.3 and place trailing payload on
    ///   the next physical line as executable source.</description></item>
    ///   <item><description>Bare CR / LF — already split off by callers but
    ///   normalized here as defense in depth.</description></item>
    ///   <item><description>Other C0 / C1 control characters except TAB
    ///   (XML 1.0 forbids most of these in text content anyway).</description></item>
    ///   <item><description>Invisible / bidi-control characters (zero-width
    ///   space, RTL override, etc.) — these are visually invisible and could
    ///   silently alter how the comment renders in tooling.</description></item>
    /// </list>
    /// Forbidden code points are replaced with the numeric character reference
    /// <c>&amp;#xNNNN;</c> so the information survives in the comment but cannot
    /// be interpreted as a line terminator by the C# lexer.
    /// </summary>
    public static string SafeXmlDocText(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            switch (c)
            {
                case '&':
                    sb.Append("&amp;");
                    continue;
                case '<':
                    sb.Append("&lt;");
                    continue;
                case '>':
                    sb.Append("&gt;");
                    continue;
                case '"':
                    sb.Append("&quot;");
                    continue;
                case '\'':
                    sb.Append("&apos;");
                    continue;
                case '\t':
                    sb.Append(c);
                    continue;
                case '\r':
                case '\n':
                    // Caller is expected to split on newlines; if any survived,
                    // collapse them to a space so they cannot terminate the
                    // `///` line.
                    sb.Append(' ');
                    continue;
            }

            if (Array.IndexOf(LineTerminators, c) >= 0)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "&#x{0:X4};", (int)c));
                continue;
            }

            // C0 (0x00–0x1F) and C1 (0x7F–0x9F) controls except TAB (handled above).
            if (c < 0x20 || (c >= 0x7F && c <= 0x9F))
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "&#x{0:X4};", (int)c));
                continue;
            }

            if (IsInvisibleOrBidi(c))
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "&#x{0:X4};", (int)c));
                continue;
            }

            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Convert an arbitrary spec-derived string into a guaranteed-valid C#
    /// identifier per ECMA-334 §6.4.2.
    ///
    /// Steps:
    /// <list type="number">
    ///   <item><description>NFKC-normalize to collapse compatibility / many
    ///   homoglyph forms.</description></item>
    ///   <item><description>Reject invisible / bidi-control characters
    ///   outright (Trojan-Source defense).</description></item>
    ///   <item><description>Replace any character outside the identifier-char
    ///   classes with <c>_</c>.</description></item>
    ///   <item><description>Prefix <c>_</c> if the first character is not a
    ///   valid identifier-start.</description></item>
    ///   <item><description>If the result is a C# reserved keyword, prefix
    ///   <c>@</c> to use the verbatim-identifier form.</description></item>
    ///   <item><description>If the input collapses to empty, return
    ///   <c>_</c>.</description></item>
    /// </list>
    /// </summary>
    public static string SafeCSharpIdentifier(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "_";

        // Reject invisible / bidi controls before normalization — NFKC may
        // legitimately collapse some compatibility characters to ASCII, which
        // would mask a Trojan-Source attempt.
        var preNorm = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (IsInvisibleOrBidi(raw[i]))
                continue; // strip
            preNorm.Append(raw[i]);
        }

        var normalized = preNorm.ToString().Normalize(NormalizationForm.FormKC);
        if (normalized.Length == 0)
            return "_";

        var sb = new StringBuilder(normalized.Length);
        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            // Treat position 0 specially: an identifier-start position must reject
            // even valid identifier-continue chars (e.g. digits) by replacing
            // with '_'. This guarantees the first character of the result is
            // always identifier-start-valid.
            var isStart = sb.Length == 0;
            if (IsValidIdentifierChar(c, isStart))
            {
                sb.Append(c);
            }
            else if (isStart && IsValidIdentifierChar(c, isStart: false))
            {
                // The char is valid in continue position but not start position
                // (a digit). Prefix '_' and keep the digit.
                sb.Append('_');
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }

        var ident = sb.ToString();

        if (CSharpReservedKeywords.Contains(ident))
            ident = "@" + ident;

        return ident;
    }

    /// <summary>
    /// Escape a string for safe inclusion inside a regular C# string literal
    /// (<c>"..."</c>). Escapes <c>\</c>, <c>"</c>, control characters, and
    /// any Unicode line-terminator (which a regular string literal forbids).
    /// </summary>
    public static string SafeCSharpStringLiteral(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    continue;
                case '"':
                    sb.Append("\\\"");
                    continue;
                case '\r':
                    sb.Append("\\r");
                    continue;
                case '\n':
                    sb.Append("\\n");
                    continue;
                case '\t':
                    sb.Append("\\t");
                    continue;
                case '\0':
                    sb.Append("\\0");
                    continue;
            }

            if (c < 0x20
                || (c >= 0x7F && c <= 0x9F)
                || Array.IndexOf(LineTerminators, c) >= 0
                || IsInvisibleOrBidi(c))
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "\\u{0:X4}", (int)c));
                continue;
            }

            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Post-emit defense in depth. Scans <paramref name="source"/> for
    /// characters that should never appear in a generated <c>.cs</c> file
    /// regardless of which code path produced it. Throws
    /// <see cref="InvalidOperationException"/> with a precise file/offset
    /// pointer if any are found.
    /// </summary>
    public static void ScanGeneratedSource(string filePath, string source)
    {
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (Array.IndexOf(LineTerminators, c) >= 0 || IsInvisibleOrBidi(c))
            {
                var msg = string.Format(
                    CultureInfo.InvariantCulture,
                    "[generator] Forbidden character U+{0:X4} at offset {1} in generated file {2}. " +
                    "Spec-controlled content must flow through SafeEmit helpers.",
                    (int)c,
                    i,
                    filePath);
                throw new InvalidOperationException(msg);
            }
        }
    }

    private static bool IsValidIdentifierChar(char c, bool isStart)
    {
        if (c == '_')
            return true;

        var cat = CharUnicodeInfo.GetUnicodeCategory(c);
        var isLetter = cat is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.LetterNumber;

        if (isStart)
            return isLetter;

        return isLetter
            || cat is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.DecimalDigitNumber
                or UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.Format;
    }

    // C# reserved keywords (ECMA-334). Contextual keywords are NOT included
    // because they are valid identifiers in most positions; we accept the
    // small risk of a name like `var` or `record` rather than mangle every
    // such occurrence.
    private static readonly HashSet<string> CSharpReservedKeywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch",
        "char", "checked", "class", "const", "continue", "decimal", "default",
        "delegate", "do", "double", "else", "enum", "event", "explicit",
        "extern", "false", "finally", "fixed", "float", "for", "foreach",
        "goto", "if", "implicit", "in", "int", "interface", "internal", "is",
        "lock", "long", "namespace", "new", "null", "object", "operator",
        "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw",
        "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
        "ushort", "using", "virtual", "void", "volatile", "while",
    ];
}
