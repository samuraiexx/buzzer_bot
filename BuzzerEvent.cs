using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuzzerBot
{
    public enum BuzzerEvent
    {
        APPROVED,
        REJECTED,
        SCHEDULE_APPROVAL,
        TIMEOUT,
        COMPLETED,
        ERROR,
        NOOP,
    }
}
