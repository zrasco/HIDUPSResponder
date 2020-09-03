using System;
using System.Collections.Generic;
using System.Text;

namespace HIDUPSResponder.Models
{
    public class AppSettings
    {
        public int PollIntervalSeconds { get; set; }
        public int SecondsBeforePowerOffExecution { get; set; }
        public List<string> PowerOffCommands { get; set; }
        public List<string> PowerOnCommands { get; set; }
    }
}
