using System.Data.Common;

namespace DevPilot.Storage;

internal static class DbCommandExtensions
{
    public static DbParameter AddParameter(this DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
        return parameter;
    }
}
