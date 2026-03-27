import 'package:freezed_annotation/freezed_annotation.dart';

part 'auth_models.freezed.dart';
part 'auth_models.g.dart';

// ---------------------------------------------------------------------------
// Login request / response
// ---------------------------------------------------------------------------

@freezed
class LoginRequest with _$LoginRequest {
  const factory LoginRequest({
    required String sapUserCode,
    required String password,
  }) = _LoginRequest;

  factory LoginRequest.fromJson(Map<String, dynamic> json) =>
      _$LoginRequestFromJson(json);
}

@freezed
class LoginResponse with _$LoginResponse {
  const factory LoginResponse({
    required String token,
    required String refreshToken,
    required String sapUserCode,
    required String role,
    required String expiresAt,
  }) = _LoginResponse;

  factory LoginResponse.fromJson(Map<String, dynamic> json) =>
      _$LoginResponseFromJson(json);
}

// ---------------------------------------------------------------------------
// Auth state (held in Riverpod)
// ---------------------------------------------------------------------------

@freezed
class AuthState with _$AuthState {
  const factory AuthState({
    required String sapUserCode,
    required String role,
    @Default(true) bool isAuthenticated,
  }) = _AuthState;

  const factory AuthState.unauthenticated() = _Unauthenticated;

  factory AuthState.fromJson(Map<String, dynamic> json) =>
      _$AuthStateFromJson(json);
}
