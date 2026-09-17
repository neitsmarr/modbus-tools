using ModbusTools.Core.Serial;

namespace ModbusTools.Components.Serial;

/// <summary>Serial settings of a form model, edited by <see cref="SerialSettingsFields"/>.</summary>
public interface ISerialSettingsForm
{
    int BaudRate { get; set; }

    int DataBits { get; set; }

    Parity Parity { get; set; }

    StopBits StopBits { get; set; }

    /// <summary>Builds the settings, or records an error under the <c>BaudRate</c> key and returns null.</summary>
    static SerialSettings? Validate(ISerialSettingsForm form, IDictionary<string, string> errors)
    {
        if (form.BaudRate <= 0)
        {
            errors[nameof(BaudRate)] = "Baud rate must be positive.";
            return null;
        }

        return new SerialSettings(form.BaudRate, form.DataBits, form.Parity, form.StopBits);
    }
}
