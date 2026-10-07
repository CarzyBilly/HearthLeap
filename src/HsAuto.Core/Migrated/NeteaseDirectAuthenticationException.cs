// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Net.Http;

namespace HsAuto.Core.Automation;
public sealed class NeteaseDirectAuthenticationException : HttpRequestException
{
    public NeteaseDirectAuthenticationException(string message) : base(message)
    {
    }

    public NeteaseDirectAuthenticationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}