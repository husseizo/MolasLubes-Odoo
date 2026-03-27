class LoginRequest {
  const LoginRequest({
    required this.sapUserCode,
    required this.password,
  });

  final String sapUserCode;
  final String password;

  Map<String, dynamic> toJson() => {
        'sapUserCode': sapUserCode,
        'password': password,
      };
}

class LoginResponse {
  const LoginResponse({
    required this.token,
    required this.refreshToken,
    required this.sapUserCode,
    required this.role,
    required this.expiresAt,
  });

  final String token;
  final String refreshToken;
  final String sapUserCode;
  final String role;
  final String expiresAt;

  factory LoginResponse.fromJson(Map<String, dynamic> json) => LoginResponse(
        token: json['token']?.toString() ?? '',
        refreshToken: json['refreshToken']?.toString() ?? '',
        sapUserCode: json['sapUserCode']?.toString() ?? '',
        role: json['role']?.toString() ?? '',
        expiresAt: json['expiresAt']?.toString() ?? '',
      );
}

class AuthState {
  const AuthState({
    required this.sapUserCode,
    required this.role,
    required this.isAuthenticated,
  });

  const AuthState.unauthenticated()
      : sapUserCode = '',
        role = '',
        isAuthenticated = false;

  final String sapUserCode;
  final String role;
  final bool isAuthenticated;
}
