using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BatterySimulator
{
    public class SimulationTimeProvider : ITimeProvider
    {
        private DateTime _time;
        private TimeSpan _timeStep;

        public SimulationTimeProvider(DateTime start, TimeSpan timeStep)
        {
            _time = start;
            _timeStep = timeStep;
        }


        public void Increment()
        {
            _time += _timeStep;
        }

        public DateTime GetTime()
        {
            return _time;
        }
    }
}
