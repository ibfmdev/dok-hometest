using System.Diagnostics.CodeAnalysis;
using Serilog.Core;
using Serilog.Events;

namespace Dok.Api.Logging;

public sealed class PlateDestructuringPolicy : IDestructuringPolicy
{
    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        if (value is Plate plate)
        {
            result = new ScalarValue(plate.Masked());
            return true;
        }
        result = null;
        return false;
    }
}
