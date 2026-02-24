# AuthenticationMiddleware Sample

This sample demonstrates how to implement **authentication and authorization middleware** to control access to your agent.

## Overview

This sample shows two complementary middlewares:
- **AuthenticationMiddleware**: Verifies credentials (who are you?)
- **AuthorizationMiddleware**: Checks permissions (what can you do?)

Both use the **short-circuit pattern** - they reject requests before reaching the agent.

## Key Features

- **API Key Validation**: Check for valid credentials
- **Role-Based Access Control (RBAC)**: Different permissions for different users
- **User Identification**: Track who made requests
- **Clear Feedback**: Tell users why they were rejected

## How It Works

```csharp
// Request flows through middleware stack:
// 1. AuthenticationMiddleware (check API key)
//    ├─ Invalid? → Reject
//    └─ Valid? → Continue
// 2. AuthorizationMiddleware (check user role)
//    ├─ Not authorized? → Reject
//    └─ Authorized? → Continue
// 3. Agent processes request
```

## Use Cases

- **API Key Protection**: Prevent unauthorized access
- **Multi-Tenant Systems**: Different permissions per user
- **Role-Based Features**: Admins vs. regular users
- **Audit Compliance**: Track who accessed the agent
- **Rate Limiting by User**: Different limits for different tiers
- **Feature Flags**: Enable/disable features per user

## Running the Sample

```bash
export OPENAI_API_KEY=sk-...
dotnet run --project samples/AuthenticationMiddleware/AuthenticationMiddleware.csproj
```

## Request Format

The sample uses a simple format to demonstrate:
```
API_KEY:<key> USER:<user_id> <actual_request>

Examples:
- API_KEY:test-key-123 USER:alice What's the weather?
- API_KEY:invalid-key USER:bob Hello
- Just a request (no authentication, will be rejected)
```

In a real application, you would:
- Extract API key from HTTP `Authorization` header
- Get user ID from JWT token claims
- Validate signatures with cryptographic keys

## Sample Output

```
Test 1: Request without API key
❌ Authentication Failed: Invalid or missing API key
Response: Authentication failed. Please provide a valid API key.

Test 2: Valid API key but unauthorized user
✅ Authentication Successful
❌ Authorization Failed: User not authorized
Response: Authorization failed. You don't have permission...

Test 3: Valid API key and authorized user (alice)
✅ Authentication Successful
👤 User: alice
📋 Roles: admin, user
Response: I'm Claude, an AI assistant...
```

## Middleware Pattern: Short-Circuit

Both middlewares use the **short-circuit pattern** because they:
1. Check conditions before allowing the request
2. Return early if checks fail (no `await next()`)
3. Only call next() if request passes all checks

Perfect for access control policies.

## Advanced Enhancements

Extend this middleware to:
- Validate JWT tokens with cryptographic signatures
- Check permissions from external auth provider (OAuth, SAML)
- Implement multi-factor authentication (MFA)
- Audit log all access attempts
- Implement fine-grained permissions (not just roles)
- Add rate limiting per user/role
- Support API scopes (specific endpoint access)
- Cache permission decisions for performance

## Production Considerations

For production deployments:
- Use **cryptographic key management** (AWS KMS, Azure Key Vault)
- **Validate token signatures** with public keys
- Implement **distributed session cache** (Redis)
- Use **circuit breaker** pattern for auth service failures
- **Encrypt sensitive data** in transit and at rest
- Log and monitor authentication failures
- Implement **progressive backoff** for failed attempts

## Related Samples

- **RateLimitingMiddleware**: Rate limiting by user
- **LoggingMiddleware**: Audit trails for compliance
- **ErrorHandlingMiddleware**: Handle auth service outages
