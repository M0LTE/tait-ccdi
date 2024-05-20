using System.Text;

namespace tait_ccdi;

public static class CcdiChecksum
{
    public static string Calculate(string commandWithoutChecksum)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(commandWithoutChecksum);

        var step1Result = Sum(bytes);
        var step2Result = GetFirst8Bits(step1Result);
        var step3Result = TwosComplement(step2Result);
        var step4Result = ConvertToHex(step3Result);

        return step4Result;
    }

    public static bool Validate(string commandWithChecksum)
    {
        string commandWithoutChecksum = commandWithChecksum[..^2];
        string checksum = commandWithChecksum[^2..];
        return string.Equals(Calculate(commandWithoutChecksum), checksum, StringComparison.OrdinalIgnoreCase);
    }

    private static int Sum(byte[] bytes)
    {
        int sum = 0;
        foreach (var b in bytes)
        {
            sum += b;
        }
        return sum;
    }

    private static byte GetFirst8Bits(int sum)
    {
        byte bits = (byte)sum;
        bits &= 0b1111_1111;
        return bits;
    }

    public static byte TwosComplement(byte first8Bits) => (byte)(0xFF - first8Bits + 1);

    public static string ConvertToHex(byte twosComplement) => twosComplement.ToString("X2");
}
