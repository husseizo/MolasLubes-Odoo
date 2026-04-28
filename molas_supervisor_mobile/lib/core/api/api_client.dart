import 'package:dio/dio.dart';

import 'package:molas_supervisor_mobile/core/api/api_exception.dart';
import 'package:molas_supervisor_mobile/core/api/auth_interceptor.dart';
import 'package:molas_supervisor_mobile/core/config/env.dart';
import 'package:molas_supervisor_mobile/core/storage/secure_storage.dart';

/// Singleton Dio client used by every feature data layer.
///
/// Constructed once and injected via Riverpod. Provides:
/// - Base URL from [Env.apiBaseUrl]
/// - `X-Api-Key` header from [Env.apiKey]
/// - [AuthInterceptor] for token attach + refresh
/// - Uniform error mapping to [ApiException] subtypes
class ApiClient {
  ApiClient({
    required SecureStorage storage,
    required void Function() onSessionExpired,
  }) {
    _dio = Dio(
      BaseOptions(
        baseUrl: Env.apiBaseUrl,
        connectTimeout: const Duration(seconds: 15),
        receiveTimeout: const Duration(seconds: 30),
        headers: {
          'X-Api-Key': Env.apiKey,
          'Content-Type': 'application/json',
          'Accept': 'application/json',
        },
      ),
    );

    _dio.interceptors.add(
      AuthInterceptor(
        storage: storage,
        dio: _dio,
        onSessionExpired: onSessionExpired,
      ),
    );

    _dio.interceptors.add(
      InterceptorsWrapper(
        onError: (err, handler) => handler.reject(_mapError(err)),
      ),
    );
  }

  late final Dio _dio;

  Dio get dio => _dio;

  // ---------------------------------------------------------------------------
  // Convenience wrappers
  // ---------------------------------------------------------------------------

  Future<Response<T>> get<T>(
    String path, {
    Map<String, dynamic>? queryParameters,
    Options? options,
  }) =>
      _dio.get<T>(path, queryParameters: queryParameters, options: options);

  Future<Response<T>> post<T>(
    String path, {
    dynamic data,
    Map<String, dynamic>? queryParameters,
    Options? options,
  }) =>
      _dio.post<T>(
        path,
        data: data,
        queryParameters: queryParameters,
        options: options,
      );

  // ---------------------------------------------------------------------------
  // Error mapping
  // ---------------------------------------------------------------------------

  static DioException _mapError(DioException err) {
    final mapped = _toApiException(err);
    return DioException(
      requestOptions: err.requestOptions,
      response: err.response,
      type: err.type,
      error: mapped,
      message: mapped.message,
    );
  }

  static ApiException _toApiException(DioException err) {
    if (err.error is ApiException) return err.error as ApiException;

    if (err.type == DioExceptionType.connectionError ||
        err.type == DioExceptionType.unknown) {
      return const NetworkException();
    }

    if (err.type == DioExceptionType.connectionTimeout ||
        err.type == DioExceptionType.sendTimeout ||
        err.type == DioExceptionType.receiveTimeout) {
      return const NetworkException('Request timed out. Check your connection.');
    }

    final status = err.response?.statusCode;
    final body = err.response?.data;
    final serverMessage = _extractMessage(body);

    // Handle null status (no response from server)
    if (status == null) {
      return UnknownApiException(serverMessage ?? 'No response from server.');
    }

    return switch (status) {
      401 => const UnauthorizedException(),
      403 => const ForbiddenException(),
      404 => const NotFoundException(),
      409 => ConflictException(serverMessage ?? 'Conflict with current state.'),
      422 => body is Map<String, dynamic> && body.containsKey('errors')
          ? ValidationException.fromErrors(
              body['errors'] as Map<String, dynamic>)
          : ValidationException(serverMessage ?? 'Validation failed.'),
      >= 500 => ServerException(serverMessage ?? 'Server error. Please try again.'),
      _ => UnknownApiException(serverMessage ?? 'An unexpected error occurred.'),
    };
  }

  static String? _extractMessage(dynamic body) {
    if (body is Map<String, dynamic>) {
      return (body['message'] ?? body['title'] ?? body['error'])?.toString();
    }
    return null;
  }
}
