// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

static class FileNames
{
    public static string Encode(string name)
    {
        var result = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (MustEscape(c) || (c == '%' && IsEscapeToken(name, i)) || (i == name.Length - 1 && (c == '.' || c == ' '))) result.Append('%').Append(((int)c).ToString("X2"));
            else result.Append(c);
        }
        string encoded = result.ToString(), stem = encoded.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] >= '1' && stem[3] <= '9'))
            encoded = "%" + ((int)encoded[0]).ToString("X2") + encoded[1..];
        return encoded;
    }
    static bool MustEscape(char c) => c < 32 || "<>:\"/\\|?*".Contains(c);
    static bool Reserved(string name)
    {
        string stem = name.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] >= '1' && stem[3] <= '9');
    }
    static bool IsEscapeToken(string name, int i)
    {
        if (i + 2 >= name.Length || !byte.TryParse(name.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out byte value)) return false;
        char c = (char)value;
        return c == '%' || MustEscape(c) || (i + 3 == name.Length && (c == '.' || c == ' ')) || (i == 0 && Reserved(c + name[(i + 3)..]));
    }
    public static string Decode(string name)
    {
        var result = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (name[i] == '%' && IsEscapeToken(name, i)) { result.Append((char)Convert.ToByte(name.Substring(i + 1, 2), 16)); i += 2; }
            else result.Append(name[i]);
        }
        string decoded = result.ToString();
        if (decoded.Length == 0 || decoded is "." or ".." || decoded.Contains('/') || decoded.Contains('\0') || Encode(decoded) != name)
            throw new ArgumentException("无效文件名或转义。");
        return decoded;
    }
}
