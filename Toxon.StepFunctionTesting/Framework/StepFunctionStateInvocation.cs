using Amazon.StepFunctions.Model;

namespace Toxon.StepFunctionTesting.Framework;

public record StepFunctionStateInvocation(StepFunctionStateContext State, StepFunctionStateResult Result, InspectionData InspectionData);