import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:molas_supervisor_mobile/app/theme/colors.dart';
import 'package:molas_supervisor_mobile/features/auth/providers.dart';
import 'package:molas_supervisor_mobile/shared/widgets/app_scaffold.dart';

class HomeScreen extends ConsumerWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final authState = ref.watch(authStateProvider);
    final userName = authState.valueOrNull?.sapUserCode ?? '';

    return AppScaffold(
      currentIndex: 0,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('Molas Supervisor'),
          actions: [
            IconButton(
              icon: CircleAvatar(
                radius: 16,
                backgroundColor: AppColors.primaryLight,
                child: Text(
                  userName.isNotEmpty ? userName[0].toUpperCase() : 'U',
                  style: const TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w600,
                    color: AppColors.primary,
                  ),
                ),
              ),
              onPressed: () => context.go('/profile'),
            ),
          ],
        ),
        body: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            // KPI Row
            // TODO: wire real counts from replenishment/execution providers
            Row(
              children: [
                Expanded(
                  child: _KpiCard(
                    label: 'Pending Approvals',
                    value: '—',
                    icon: Icons.pending_actions_outlined,
                    color: AppColors.statusPending,
                    onTap: () => context.go('/replenishment/inbox'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: _KpiCard(
                    label: 'Failed Executions',
                    value: '—',
                    icon: Icons.error_outline,
                    color: AppColors.statusFailed,
                    onTap: () => context.go('/executions'),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 24),
            // Quick Actions
            const Text(
              'Quick Actions',
              style: TextStyle(
                fontSize: 16,
                fontWeight: FontWeight.w600,
                color: AppColors.textPrimary,
              ),
            ),
            const SizedBox(height: 12),
            _QuickActionButton(
              label: 'Open Inbox',
              subtitle: 'Review pending approval requests',
              icon: Icons.inbox_outlined,
              onTap: () => context.go('/replenishment/inbox'),
            ),
            const SizedBox(height: 8),
            _QuickActionButton(
              label: 'View Executions',
              subtitle: 'Monitor execution status',
              icon: Icons.play_circle_outline,
              onTap: () => context.go('/executions'),
            ),
            const SizedBox(height: 8),
            _QuickActionButton(
              label: 'View Alerts',
              subtitle: 'Check failed and partial transfers',
              icon: Icons.notifications_outlined,
              onTap: () => context.go('/alerts'),
            ),
            const SizedBox(height: 24),
            // Today section
            const Text(
              'Today',
              style: TextStyle(
                fontSize: 16,
                fontWeight: FontWeight.w600,
                color: AppColors.textPrimary,
              ),
            ),
            const SizedBox(height: 12),
            Card(
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Row(
                      children: [
                        Icon(Icons.wb_sunny_outlined, color: AppColors.accent),
                        SizedBox(width: 8),
                        Text(
                          'All caught up',
                          style: TextStyle(
                            fontWeight: FontWeight.w600,
                            color: AppColors.textPrimary,
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 6),
                    Text(
                      'Tap "Open Inbox" to review any pending replenishment requests.',
                      style: TextStyle(
                        fontSize: 13,
                        color: AppColors.textSecondary,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _KpiCard extends StatelessWidget {
  const _KpiCard({
    required this.label,
    required this.value,
    required this.icon,
    required this.color,
    required this.onTap,
  });

  final String label;
  final String value;
  final IconData icon;
  final Color color;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(8),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(icon, color: color, size: 28),
              const SizedBox(height: 8),
              Text(
                value,
                style: TextStyle(
                  fontSize: 28,
                  fontWeight: FontWeight.w700,
                  color: color,
                ),
              ),
              const SizedBox(height: 4),
              Text(
                label,
                style: const TextStyle(
                  fontSize: 12,
                  color: AppColors.textSecondary,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _QuickActionButton extends StatelessWidget {
  const _QuickActionButton({
    required this.label,
    required this.subtitle,
    required this.icon,
    required this.onTap,
  });

  final String label;
  final String subtitle;
  final IconData icon;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: ListTile(
        onTap: onTap,
        leading: Icon(icon, color: AppColors.primary),
        title: Text(
          label,
          style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14),
        ),
        subtitle: Text(
          subtitle,
          style: const TextStyle(fontSize: 12, color: AppColors.textSecondary),
        ),
        trailing: const Icon(Icons.chevron_right, color: AppColors.textMuted),
      ),
    );
  }
}
