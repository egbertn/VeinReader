using Handlezer.Api.Application;
using System;
using Xunit;

namespace Handlezer.Tests;

public sealed class ApiKeyServiceTests
{
    [Fact]
    public void NormalizeScopesDefaultsToApiScope()
    {
        var scopes = ApiKeyService.NormalizeScopes(null);

        Assert.Single(scopes);
        Assert.Equal(ApiKeyScopes.Api, scopes[0], ignoreCase: false, ignoreLineEndingDifferences: false, ignoreWhiteSpaceDifferences: false);
    }

    [Fact]
    public void NormalizeScopesIgnoresCaseAndWhitespace()
    {
        var scopes = ApiKeyService.NormalizeScopes(["  HANDLEZER.ADMIN  ", "handlezer.api", " HANDLEZER.ADMIN "]);

        Assert.Collection(scopes,
            scope => Assert.Equal(ApiKeyScopes.Admin, scope, ignoreCase: false, ignoreLineEndingDifferences: false, ignoreWhiteSpaceDifferences: false),
            scope => Assert.Equal(ApiKeyScopes.Api, scope, ignoreCase: false, ignoreLineEndingDifferences: false, ignoreWhiteSpaceDifferences: false));
    }

    [Fact]
    public void NormalizeScopesRejectsUnknownScopes()
    {
        var exception = Assert.Throws<ArgumentException>(() => ApiKeyService.NormalizeScopes(["unknown.scope"]));

        Assert.Contains("unknown.scope", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateBuildsOpaqueTokenAndMatchingHash()
    {
        var tokenService = new ApiKeyTokenService();

        var material = tokenService.Create();

        Assert.StartsWith($"hzr_{material.KeyId}.", material.Token, StringComparison.Ordinal);
        Assert.True(tokenService.SecretsMatch(material.Secret, tokenService.ComputeSecretHash(material.Secret)));
    }
}