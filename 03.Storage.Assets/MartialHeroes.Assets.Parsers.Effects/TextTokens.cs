namespace MartialHeroes.Assets.Parsers.Effects;

internal ref struct TokenCursor
{
    private readonly ReadOnlySpan<char> _text;
    private int _pos;

    public TokenCursor(ReadOnlySpan<char> text)
    {
        _text = text;
        _pos = 0;
    }

    public bool TryNext(out ReadOnlySpan<char> token)
    {
        while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            _pos++;

        if (_pos >= _text.Length)
        {
            token = default;
            return false;
        }

        var start = _pos;
        while (_pos < _text.Length && !char.IsWhiteSpace(_text[_pos]))
            _pos++;

        token = _text.Slice(start, _pos - start);
        return true;
    }
}

internal ref struct LineCursor
{
    private readonly ReadOnlySpan<char> _text;
    private int _pos;

    public LineCursor(ReadOnlySpan<char> text)
    {
        _text = text;
        _pos = 0;
    }

    public bool TryNext(out ReadOnlySpan<char> line)
    {
        if (_pos >= _text.Length)
        {
            line = default;
            return false;
        }

        var start = _pos;
        while (_pos < _text.Length && _text[_pos] != '\n')
            _pos++;

        var end = _pos;
        if (end > start && _text[end - 1] == '\r')
            end--;

        line = _text[start..end];

        if (_pos < _text.Length)
            _pos++;

        return true;
    }
}