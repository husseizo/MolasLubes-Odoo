import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

class AppScaffold extends StatelessWidget {
  const AppScaffold({
    super.key,
    required this.child,
    required this.currentIndex,
  });

  final Widget child;
  final int currentIndex;

  static const _routes = [
    '/home',
    '/replenishment/inbox',
    '/executions',
    '/alerts',
    '/profile',
  ];

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: child,
      bottomNavigationBar: NavigationBar(
        selectedIndex: currentIndex,
        onDestinationSelected: (i) => context.go(_routes[i]),
        destinations: const [
          NavigationDestination(icon: Icon(Icons.home_outlined), selectedIcon: Icon(Icons.home), label: 'Home'),
          NavigationDestination(icon: Icon(Icons.inbox_outlined), selectedIcon: Icon(Icons.inbox), label: 'Inbox'),
          NavigationDestination(icon: Icon(Icons.play_circle_outline), selectedIcon: Icon(Icons.play_circle), label: 'Executions'),
          NavigationDestination(icon: Icon(Icons.notifications_outlined), selectedIcon: Icon(Icons.notifications), label: 'Alerts'),
          NavigationDestination(icon: Icon(Icons.person_outline), selectedIcon: Icon(Icons.person), label: 'Profile'),
        ],
      ),
    );
  }
}
