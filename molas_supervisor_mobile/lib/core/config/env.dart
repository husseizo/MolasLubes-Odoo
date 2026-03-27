/// Compile-time environment configuration.
///
/// Pass values via `--dart-define`:
///   flutter run \
///     --dart-define=API_BASE_URL=https://api.example.com \
///     --dart-define=API_KEY=your_api_key_here \
///     --dart-define=ENVIRONMENT=Live
class Env {
  Env._();

  static const String apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'https://api.molaslubes.co.za',
  );

  static const String apiKey = String.fromEnvironment(
    'API_KEY',
    defaultValue: '',
  );

  static const String environment = String.fromEnvironment(
    'ENVIRONMENT',
    defaultValue: 'Test',
  );

  static bool get isLive => environment == 'Live';
  static bool get isTest => !isLive;
}
