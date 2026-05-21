namespace HLSLInterpreter.Debugger.Mvu;

// Base of the closed message set. Every event the app can produce is a Msg.
// Concrete cases are defined in phase 2, alongside the update function.
public abstract record Msg;
