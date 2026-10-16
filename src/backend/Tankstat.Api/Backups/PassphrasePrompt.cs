using System.Text;
using Tankstat.Domain;

namespace Tankstat.Api.Backups;

/// <summary>
/// Where the backup commands read a passphrase from: never an argument (the process list and the shell history would keep it). At a terminal
/// it is typed without an echo (<see cref="TerminalPrompt"/>); piped in, it is a line of the standard input (<see cref="LinePrompt"/>).
/// </summary>
internal interface IPassphrasePrompt
{
    /// <summary>One passphrase; <paramref name="label"/> is what a terminal asks with ("Passphrase:", "Repeat it:").</summary>
    string Read(string label);
}

/// <summary>A line of the standard input per passphrase: a new one is two lines (<c>printf '%s\n%s\n' "$P" "$P" | ...</c>).</summary>
internal sealed class LinePrompt(TextReader input) : IPassphrasePrompt
{
    public string Read(string label)
    {
        // A byte order mark (a file saved by a Windows editor and piped in) is no part of what was typed: kept, it would derive another key.
        var passphrase = input.ReadLine()?.TrimStart((char)0xFEFF).TrimEnd('\r');
        if (string.IsNullOrEmpty(passphrase)) throw new DomainException("backup.passphraseRequired", "A passphrase is expected on the standard input (one line each).");
        return passphrase;
    }
}

/// <summary>
/// Typed at the terminal without an echo: the question on the standard error (the standard output may be a file the keys go to), each key
/// read with <see cref="Console.ReadKey(bool)"/>, Backspace taking the last character back, Enter ending it; Ctrl+C stops the command. It
/// keeps what a line of <see cref="LinePrompt"/> and the Backups page keep, a Tab (pasted) included: the same passphrase must give the same key
/// whichever way it came. Only the other control characters (what a special key gives) are left out.
/// </summary>
internal sealed class TerminalPrompt(TextWriter error) : IPassphrasePrompt
{
    public string Read(string label)
    {
        error.Write($"{label} ");
        var typed = new StringBuilder();
        while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace)
            {
                if (typed.Length > 0) typed.Length--;
            }
            else if (key.KeyChar == '\t' || !char.IsControl(key.KeyChar)) typed.Append(key.KeyChar);
        }
        error.WriteLine();
        if (typed.Length == 0) throw new DomainException("backup.passphraseRequired", "A passphrase is expected.");
        return typed.ToString();
    }
}
