namespace AutoTyper;

// Each method returns false if the operating system did not accept the input.
public interface IKeySender
{
    bool SendText(string text);
    bool SendEnter();
    bool SendTab();
}
