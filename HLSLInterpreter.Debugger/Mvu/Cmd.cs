namespace HLSLInterpreter.Debugger.Mvu;

// Base of the closed command set. A Cmd is a description of a side effect that
// update returns and the effect runner carries out. Concrete cases live in
// Commands.cs.
public abstract record Cmd;
