namespace tait_ccdi;

public static class SerialExtensions
{
    /*public static bool TryReadTo(this ISerialPort serialPort, string value, out string output, TimeSpan? timeSpan = null)
    {
        var oldTimeout = serialPort.ReadTimeout;
        if (timeSpan.HasValue)
        {
            serialPort.ReadTimeout = (int)timeSpan.Value.TotalMilliseconds;
        }

        try
        {
            output = serialPort.ReadTo(value);
            return true;
        }
        catch (TimeoutException)
        {
            output = "";
            return false;
        }
        catch (OperationCanceledException)
        {
            output = "";
            return false;
        }
        finally
        {
            if (timeSpan.HasValue)
            {
                serialPort.ReadTimeout = oldTimeout;
            }
        }
    }*/

    public static char[] ReadChars(this ISerialPort serialPort, int count)
    {
        var result = new char[count];
        for(int i = 0; i < count; i++)
        {
            char c = (char)serialPort.ReadByte();
            if (c >= 32 && c <= 126 || c == '\r')
            {
                result[i] = c;
            }
            else
            {
                throw new Exception("Invalid character: " + c + " (" + (int)c + ")");
            }
        }
        return result;
    }
}
