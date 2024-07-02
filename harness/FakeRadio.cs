using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tait_ccdi;

namespace harness
{
    public class FakeRadio : ISerialPort
    {
        private string? nextRead;

        public int ReadTimeout { get; set; }
        public void DiscardInBuffer() { }
        public void DiscardOutBuffer() { }
        public void Dispose() { }
        public void Open() { }
        public int ReadByte()
        {
            throw new NotImplementedException();
        }
        public string? ReadExisting() => "";
        public string ReadTo(string value)
        {
            if (value == "\r." && nextRead != null)
            {
                return nextRead;
            }

            return "";
        }
        public void WriteLine(string value)
        {
            if (value == QueryCommands.ModelAndCcdiVersion)
            {
                nextRead = ".m_fakeradio";
            }
            else
            {
                nextRead = null;
            }
        }
    }
}
