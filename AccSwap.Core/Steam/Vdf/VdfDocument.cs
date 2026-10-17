using System.Text;

namespace AccSwap.Core.Steam.Vdf;

/// <summary>
/// Parser and writer for Valve's KeyValues (VDF) text format, used by
/// loginusers.vdf. The parser preserves all fields it does not understand,
/// so a round-trip never drops launcher data. A malformed file throws
/// <see cref="VdfFormatException"/> instead of producing a corrupt write.
/// </summary>
public static class VdfDocument
{
    public static VdfNode Parse(string text)
    {
        var tokenizer = new Tokenizer(text);
        VdfNode root = ParseNode(tokenizer, topLevel: true);
        return root;
    }

    public static string Serialize(VdfNode root)
    {
        var builder = new StringBuilder();
        WriteNode(builder, root, indent: 0, skipRootBraces: true);
        return builder.ToString();
    }

    private static VdfNode ParseNode(Tokenizer tokenizer, bool topLevel)
    {
        var node = new VdfNode();
        while (true)
        {
            Token? token = tokenizer.Next();
            if (token is null)
            {
                if (topLevel)
                {
                    return node;
                }

                throw new VdfFormatException("Unexpected end of VDF inside a node.");
            }

            if (token.Value.Kind == TokenKind.CloseBrace)
            {
                if (topLevel)
                {
                    throw new VdfFormatException("Unbalanced '}' at top level.");
                }

                return node;
            }

            if (token.Value.Kind != TokenKind.Text)
            {
                throw new VdfFormatException($"Expected a key, found '{token.Value.Text}'.");
            }

            string key = token.Value.Text;
            Token? next = tokenizer.Next()
                ?? throw new VdfFormatException($"Key '{key}' has no value.");

            switch (next.Value.Kind)
            {
                case TokenKind.OpenBrace:
                    node.Add(key, ParseNode(tokenizer, topLevel: false));
                    break;
                case TokenKind.Text:
                    node.Add(key, new VdfString(next.Value.Text));
                    break;
                default:
                    throw new VdfFormatException($"Key '{key}' followed by '}}'.");
            }
        }
    }

    private static void WriteNode(StringBuilder builder, VdfNode node, int indent, bool skipRootBraces)
    {
        string pad = new('\t', indent);
        if (!skipRootBraces)
        {
            builder.Append(pad).AppendLine("{");
        }

        foreach (VdfEntry entry in node.Entries)
        {
            if (entry.Value is VdfNode nested)
            {
                builder.Append(pad).Append(Escape(entry.Key)).AppendLine();
                WriteNode(builder, nested, indent + 1, skipRootBraces: false);
            }
            else if (entry.Value is VdfString str)
            {
                builder.Append(pad)
                    .Append(Escape(entry.Key))
                    .Append('\t')
                    .Append(Escape(str.Value))
                    .AppendLine();
            }
        }

        if (!skipRootBraces)
        {
            builder.Append(pad).AppendLine("}");
        }
    }

    private static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length + 8);
        builder.Append('"');
        foreach (char c in text)
        {
            builder.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c,
            });
        }

        builder.Append('"');
        return builder.ToString();
    }

    private enum TokenKind
    {
        Text,
        OpenBrace,
        CloseBrace,
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private sealed class Tokenizer(string text)
    {
        private int _position;

        public Token? Next()
        {
            SkipWhitespaceAndComments();
            if (_position >= text.Length)
            {
                return null;
            }

            char current = text[_position];
            if (current == '{')
            {
                _position++;
                return new Token(TokenKind.OpenBrace, "{");
            }

            if (current == '}')
            {
                _position++;
                return new Token(TokenKind.CloseBrace, "}");
            }

            if (current == '"')
            {
                return new Token(TokenKind.Text, ReadQuoted());
            }

            return new Token(TokenKind.Text, ReadBare());
        }

        private void SkipWhitespaceAndComments()
        {
            while (_position < text.Length)
            {
                char c = text[_position];
                if (char.IsWhiteSpace(c))
                {
                    _position++;
                }
                else if (c == '/' && _position + 1 < text.Length && text[_position + 1] == '/')
                {
                    _position += 2;
                    while (_position < text.Length && text[_position] != '\n')
                    {
                        _position++;
                    }
                }
                else if (c == '/' && _position + 1 < text.Length && text[_position + 1] == '*')
                {
                    _position += 2;
                    int end = text.IndexOf("*/", _position, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        throw new VdfFormatException("Unterminated /* comment.");
                    }

                    _position = end + 2;
                }
                else
                {
                    return;
                }
            }
        }

        private string ReadQuoted()
        {
            _position++; // skip opening quote
            var builder = new StringBuilder();
            while (_position < text.Length)
            {
                char c = text[_position++];
                if (c == '"')
                {
                    return builder.ToString();
                }

                if (c == '\\' && _position < text.Length)
                {
                    char escaped = text[_position++];
                    builder.Append(escaped switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => escaped,
                    });
                }
                else
                {
                    builder.Append(c);
                }
            }

            throw new VdfFormatException("Unterminated quoted string.");
        }

        private string ReadBare()
        {
            int start = _position;
            while (_position < text.Length)
            {
                char c = text[_position];
                if (char.IsWhiteSpace(c) || c == '{' || c == '}' || c == '"')
                {
                    break;
                }

                _position++;
            }

            if (_position == start)
            {
                throw new VdfFormatException($"Unexpected character '{text[_position]}'.");
            }

            return text[start.._position];
        }
    }
}

public sealed class VdfFormatException(string message) : FormatException(message);
