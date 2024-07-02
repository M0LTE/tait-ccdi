namespace tait_ccdi;

public static class SerialExtensions
{
    public static bool TryReadTo(this ISerialPort serialPort, string value, out string output, TimeSpan? timeSpan = null)
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
    }
}
