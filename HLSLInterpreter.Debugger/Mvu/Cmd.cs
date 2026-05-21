namespace HLSLInterpreter.Debugger.Mvu;

// Base of the closed command set. A Cmd is a description of a side effect that
// update returns and the effect runner carries out. Concrete cases are defined
// in phase 3, alongside their handlers.
public abstract record Cmd;
