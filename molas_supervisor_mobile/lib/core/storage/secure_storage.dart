import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Keys used in secure storage.
abstract final class StorageKeys {
  static const String jwtToken = 'jwt_token';
  static const String refreshToken = 'refresh_token';
  static const String sapUserCode = 'sap_user_code';
  static const String sapRole = 'sap_role';
}

/// Wrapper around [FlutterSecureStorage].
///
/// All reads return `null` if the key is absent.
class SecureStorage {
  SecureStorage() : _storage = const FlutterSecureStorage(
    aOptions: AndroidOptions(encryptedSharedPreferences: true),
  );

  final FlutterSecureStorage _storage;

  // ---------------------------------------------------------------------------
  // Primitives
  // ---------------------------------------------------------------------------

  Future<void> write(String key, String value) =>
      _storage.write(key: key, value: value);

  Future<String?> read(String key) => _storage.read(key: key);

  Future<void> delete(String key) => _storage.delete(key: key);

  Future<void> deleteAll() => _storage.deleteAll();

  // ---------------------------------------------------------------------------
  // Auth-specific helpers
  // ---------------------------------------------------------------------------

  Future<void> saveSession({
    required String jwtToken,
    required String refreshToken,
    required String sapUserCode,
    required String role,
  }) async {
    await Future.wait([
      write(StorageKeys.jwtToken, jwtToken),
      write(StorageKeys.refreshToken, refreshToken),
      write(StorageKeys.sapUserCode, sapUserCode),
      write(StorageKeys.sapRole, role),
    ]);
  }

  Future<String?> get jwtToken => read(StorageKeys.jwtToken);
  Future<String?> get refreshToken => read(StorageKeys.refreshToken);
  Future<String?> get sapUserCode => read(StorageKeys.sapUserCode);
  Future<String?> get sapRole => read(StorageKeys.sapRole);

  Future<void> clearSession() => deleteAll();
}
