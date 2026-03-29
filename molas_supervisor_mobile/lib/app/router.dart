import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import '../features/auth/providers.dart';
import '../features/auth/presentation/login_screen.dart';
import '../features/home/presentation/home_screen.dart';
import '../features/replenishment/presentation/inbox_screen.dart';
import '../features/replenishment/presentation/request_detail_screen.dart';
import '../features/executions/presentation/execution_status_screen.dart';
import '../features/alerts/presentation/alerts_screen.dart';
import '../features/profile/presentation/profile_screen.dart';

final routerProvider = Provider<GoRouter>((ref) {
  final authState = ref.watch(authStateProvider);
  return GoRouter(
    initialLocation: '/home',
    redirect: (context, state) {
      // authState is loading — don't redirect yet
      if (authState.isLoading) return null;
      // Consider authenticated if we have a sapUserCode in the resolved state
      final isAuthenticated = authState.hasValue &&
          authState.value != null &&
          authState.value!.isAuthenticated;
      final isLoginPage = state.matchedLocation == '/login';
      if (!isAuthenticated && !isLoginPage) return '/login';
      if (isAuthenticated && isLoginPage) return '/home';
      return null;
    },
    routes: [
      GoRoute(path: '/login',  builder: (ctx, s) => const LoginScreen()),
      GoRoute(path: '/home',   builder: (ctx, s) => const HomeScreen()),
      GoRoute(
        path: '/replenishment/inbox',
        builder: (ctx, s) => const InboxScreen(),
      ),
      GoRoute(
        path: '/replenishment/:ref',
        builder: (ctx, s) => RequestDetailScreen(
          requestRef: s.pathParameters['ref']!,
        ),
      ),
      GoRoute(
        path: '/executions',
        builder: (ctx, s) => const ExecutionStatusScreen(),
      ),
      GoRoute(path: '/alerts',  builder: (ctx, s) => const AlertsScreen()),
      GoRoute(path: '/profile', builder: (ctx, s) => const ProfileScreen()),
    ],
  );
});
