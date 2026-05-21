namespace HLSLInterpreter.Debugger.Mvu;

// Base of the closed message set. Every event the app can produce is a Msg.
// Concrete cases live in Messages.cs.
public abstract record Msg;
