using System;

namespace Console.Basic;

public record Money(decimal Amount, string Currency);

public interface IGreeter
{
    string Greet(string name);
}

public class Greeter : IGreeter
{
    private readonly string _prefix;

    public Greeter(string prefix) => _prefix = prefix;

    public string Prefix => _prefix;

    public string Greet(string name)
    {
        var msg = Format(name);
        return msg;
    }

    private string Format(string name) => $"{_prefix}, {name}!";

    public bool TryParseAmount(string s, out decimal amount) =>
        decimal.TryParse(s, out amount);

    public async System.Threading.Tasks.Task<int> CountAsync() => await Delay();

    private async System.Threading.Tasks.Task<int> Delay()
    {
        await System.Threading.Tasks.Task.Yield();
        return 1;
    }
}
