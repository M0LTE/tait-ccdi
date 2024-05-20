using System.Diagnostics;
using System.Text;
using tait_ccdi;

var radio = new TaitRadio("COM3", 115200);

//Console.WriteLine("Press enter to queue command");

var sw = Stopwatch.StartNew();
var sb = new StringBuilder();
while (sw.ElapsedMilliseconds < 15000)
{
    //Console.ReadLine();
    var rssi = await radio.GetRawRssi();
    //Console.SetCursorPosition(0, 0);
    //Console.WriteLine(rssi);
    sb.AppendLine($"{DateTime.Now.ToString("HH:mm:ss.fff")},{rssi}");
}

File.WriteAllText("C:\\Users\\me\\Desktop\\rssi.csv", sb.ToString());