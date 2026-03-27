import 'package:dio/dio.dart';

import 'package:molas_supervisor_mobile/core/api/api_exception.dart';
import 'package:molas_supervisor_mobile/core/config/env.dart';
import 'package:molas_supervisor_mobile/core/storage/secure_storage.dart';

/// Dio interceptor that:
/// 1. Attaches `Authorization: Bearer <token>` to every request (if a token
///    is stored).
/// 2. On 401: attempts a token refresh.
///    - Success → retries the original request with the new token.
///    - Failure → clears storage and rethrows [UnauthorizedException].
class AuthInterceptor extends Interceptor {
  AuthInterceptor({
    required SecureStorage storage,
    required Dio dio,
    required void Function() onSessionExpired,
  })  : _storage = storage,
        _dio = dio,
        _onSessionExpired = onSessionExpired;

  final SecureStorage _storage;
  final Dio _dio;
  final void Function() _onSessionExpired;

  bool _isRefreshing = false;

  @override
  Future<void> onRequest(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    final token = await _storage.jwtToken;
    if (token != null) {
      options.headers['Authorization'] = 'Bearer $token';
    }
    handler.next(options);
  }

  @override
  Future<void> onError(
    DioException err,
    ErrorInterceptorHandler handler,
  ) async {
    final response = err.response;

    if (response?.statusCode == 401 && !_isRefreshing) {
      _isRefreshing = true;

      try {
        final refreshToken = await _storage.refreshToken;
        if (refreshToken == null) {
          _handleSessionExpired(handler, err);
          return;
        }

        final refreshDio = Dio(
          BaseOptions(
            baseUrl: Env.apiBaseUrl,
            headers: {
              'X-Api-Key': Env.apiKey,
              'Content-Type': 'application/json',
            },
          ),
        );

        final refreshResponse = await refreshDio.post<Map<String, dynamic>>(
          '/api/auth/refresh',
          data: {'refreshToken': refreshToken},
        );

        final data = refreshResponse.data!;
        await _storage.saveSession(
          jwtToken: data['token'] as String,
          refreshToken: data['refreshToken'] as String,
          sapUserCode: data['sapUserCode'] as String,
          role: data['role'] as String,
        );

        // Retry the original request with the new token.
        final newToken = data['token'] as String;
        final opts = err.requestOptions;
        opts.headers['Authorization'] = 'Bearer $newToken';

        final retryResponse = await _dio.fetch<dynamic>(opts);
        handler.resolve(retryResponse);
      } on DioException catch (_) {
        _handleSessionExpired(handler, err);
      } finally {
        _isRefreshing = false;
      }
    } else {
      handler.next(err);
    }
  }

  void _handleSessionExpired(ErrorInterceptorHandler handler, DioException err) {
    _storage.clearSession();
    _onSessionExpired();
    handler.reject(
      DioException(
        requestOptions: err.requestOptions,
        error: const UnauthorizedException(),
        type: DioExceptionType.badResponse,
        response: err.response,
      ),
    );
  }
}
