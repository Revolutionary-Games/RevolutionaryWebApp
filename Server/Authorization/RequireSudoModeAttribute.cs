namespace RevolutionaryWebApp.Server.Authorization;

using System;

/// <summary>
///   Requires user to have recently confirmed their password / login to do very destructive actions. NOT IMPLEMENTED.
///   TODO: implement this and implement a nice way for client method requests to retry with proper authorization.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class RequireSudoModeAttribute : Attribute
{
}
