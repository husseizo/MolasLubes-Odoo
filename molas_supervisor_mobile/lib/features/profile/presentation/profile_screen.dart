import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:molas_supervisor_mobile/app/theme/colors.dart';
import 'package:molas_supervisor_mobile/core/config/env.dart';
import 'package:molas_supervisor_mobile/features/auth/providers.dart';

class ProfileScreen extends ConsumerWidget {
  const ProfileScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final authState = ref.watch(authStateProvider).valueOrNull;
    final sapUserCode = authState?.sapUserCode ?? '—';
    final role = authState?.role ?? '—';
    final isLive = Env.isLive;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Profile'),
        leading: BackButton(onPressed: () => context.go('/home')),
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          // Avatar + name
          Center(
            child: Column(
              children: [
                const SizedBox(height: 16),
                CircleAvatar(
                  radius: 36,
                  backgroundColor: AppColors.primaryLight,
                  child: Text(
                    sapUserCode.isNotEmpty ? sapUserCode[0].toUpperCase() : 'U',
                    style: const TextStyle(
                      fontSize: 28,
                      fontWeight: FontWeight.w700,
                      color: AppColors.primary,
                    ),
                  ),
                ),
                const SizedBox(height: 12),
                Text(
                  sapUserCode,
                  style: const TextStyle(
                    fontSize: 18,
                    fontWeight: FontWeight.w600,
                    color: AppColors.textPrimary,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  role,
                  style: const TextStyle(
                    fontSize: 13,
                    color: AppColors.textSecondary,
                  ),
                ),
                const SizedBox(height: 8),
                // Environment badge
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 3),
                  decoration: BoxDecoration(
                    color: isLive ? Colors.green.shade50 : Colors.orange.shade50,
                    borderRadius: BorderRadius.circular(12),
                    border: Border.all(
                      color: isLive ? Colors.green.shade300 : Colors.orange.shade300,
                    ),
                  ),
                  child: Text(
                    Env.environment,
                    style: TextStyle(
                      fontSize: 11,
                      fontWeight: FontWeight.w600,
                      color: isLive ? Colors.green.shade700 : Colors.orange.shade700,
                    ),
                  ),
                ),
                const SizedBox(height: 24),
              ],
            ),
          ),
          // Info card
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                children: [
                  _ProfileRow(label: 'SAP User Code', value: sapUserCode),
                  const Divider(height: 24),
                  _ProfileRow(label: 'Role', value: role),
                  const Divider(height: 24),
                  _ProfileRow(label: 'Environment', value: Env.environment),
                  const Divider(height: 24),
                  _ProfileRow(label: 'API Base URL', value: Env.apiBaseUrl),
                ],
              ),
            ),
          ),
          const SizedBox(height: 24),
          // Sign out
          OutlinedButton.icon(
            style: OutlinedButton.styleFrom(
              foregroundColor: AppColors.statusRejected,
              side: const BorderSide(color: AppColors.statusRejected),
              minimumSize: const Size.fromHeight(44),
            ),
            icon: const Icon(Icons.logout),
            label: const Text('Sign Out'),
            onPressed: () async {
              final repo = ref.read(authRepositoryProvider);
              await repo.logout();
              ref.invalidate(authStateProvider);
              if (context.mounted) context.go('/login');
            },
          ),
        ],
      ),
    );
  }
}

class _ProfileRow extends StatelessWidget {
  const _ProfileRow({required this.label, required this.value});
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        SizedBox(
          width: 120,
          child: Text(
            label,
            style: const TextStyle(fontSize: 13, color: AppColors.textSecondary),
          ),
        ),
        Expanded(
          child: Text(
            value,
            style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w500),
            overflow: TextOverflow.ellipsis,
          ),
        ),
      ],
    );
  }
}
