/// Typed exception hierarchy for API errors.
sealed class ApiException implements Exception {
  const ApiException(this.message);
  final String message;

  @override
  String toString() => 'ApiException: $message';
}

/// HTTP 401 — credentials rejected or token expired after refresh attempt.
class UnauthorizedException extends ApiException {
  const UnauthorizedException([super.message = 'Unauthorised. Please log in again.']);
}

/// HTTP 403 — authenticated but not allowed.
class ForbiddenException extends ApiException {
  const ForbiddenException([super.message = 'Access denied.']);
}

/// HTTP 404 — resource not found.
class NotFoundException extends ApiException {
  const NotFoundException([super.message = 'Resource not found.']);
}

/// HTTP 409 / business-rule violation.
class ConflictException extends ApiException {
  const ConflictException(super.message);
}

/// HTTP 422 / validation rejected by server.
class ValidationException extends ApiException {
  const ValidationException(super.message);

  factory ValidationException.fromErrors(Map<String, dynamic> errors) {
    final buffer = StringBuffer();
    errors.forEach((field, messages) {
      if (messages is List) {
        buffer.writeln('$field: ${messages.join(', ')}');
      } else {
        buffer.writeln('$field: $messages');
      }
    });
    return ValidationException(buffer.toString().trim());
  }
}

/// HTTP 5xx or network timeout.
class ServerException extends ApiException {
  const ServerException([super.message = 'Server error. Please try again.']);
}

/// No network / socket exception.
class NetworkException extends ApiException {
  const NetworkException([super.message = 'Network error. Check your connection.']);
}

/// Any other non-typed error.
class UnknownApiException extends ApiException {
  const UnknownApiException([super.message = 'An unexpected error occurred.']);
}
