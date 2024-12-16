using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Exceptions.ReflectionExceptions
{
    public class InvokeMethodException : ReflectionException
    {
        public InvokeMethodException(string message) : base(message)
        {
        }
    }
}