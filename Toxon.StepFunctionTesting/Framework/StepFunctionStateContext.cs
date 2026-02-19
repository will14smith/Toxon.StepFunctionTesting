namespace Toxon.StepFunctionTesting.Framework;

public record StepFunctionStateContext(  
    string StateName,
    string QueryLanguage,
    string Input,
    string Variables
);