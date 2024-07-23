namespace tait_ccdi;

internal static class Extensions
{
    public static char[] ReadPrintableChars(this ISerialPort serialPort, int count)
    {
        var result = new char[count];
        for (int i = 0; i < count; i++)
        {
            char c = (char)serialPort.ReadByte();
            if (c.IsPrintable() || c == '\r')
            {
                result[i] = c;
            }
            else
            {
                throw new Exception("Unprintable character: " + c + " " + (int)c);
            }
        }
        return result;
    }

    public static bool IsPrintable(this char c) => c >= 32 && c <= 126;

    public static bool TryReadChar(this ISerialPort serialPort, TimeSpan timeout, out char value)
    {
        var oldTimeout = serialPort.ReadTimeout;
        serialPort.ReadTimeout = timeout;
        try
        {
            value = (char)serialPort.ReadByte();
            return true;
        }
        catch (TimeoutException)
        {
            value = default;
            return false;
        }
        finally
        {
            serialPort.ReadTimeout = oldTimeout;
        }
    }

    public static string ToHex(this int b)
    {
        var hex = b.ToString("X").ToLower();
        if (hex.Length == 1)
        {
            hex = "0" + hex;
        }
        return hex;
    }
}