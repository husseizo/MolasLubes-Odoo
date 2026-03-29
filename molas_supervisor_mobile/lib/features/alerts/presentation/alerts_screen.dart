import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:molas_supervisor_mobile/app/theme/colors.dart';
import 'package:molas_supervisor_mobile/core/utils/formatters.dart';
import 'package:molas_supervisor_mobile/features/executions/providers.dart';
import 'package:molas_supervisor_mobile/features/replenishment/data/models/request_summary.dart';
import 'package:molas_supervisor_mobile/shared/widgets/app_scaffold.dart';
import 'package:molas_supervisor_mobile/shared/widgets/empty_view.dart';
import 'package:molas_supervisor_mobile/shared/widgets/error_view.dart';
import 'package:molas_supervisor_mobile/shared/widgets/loading_view.dart';

class AlertsScreen extends ConsumerWidget {
  const AlertsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final asyncData = ref.watch(partialFailedProvider);

    return AppScaffold(
      currentIndex: 3,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('Alerts'),
          leading: BackButton(onPressed: () => context.go('/home')),
        ),
        body: RefreshIndicator(
          onRefresh: () async => ref.invalidate(partialFailedProvider),
          child: asyncData.when(
            loading: () => const LoadingView(),
            error: (err, _) => ErrorView(
              message: err.toString().replaceFirst('Exception: ', ''),
              onRetry: () => ref.invalidate(partialFailedProvider),
            ),
            data: (items) {
              if (items.isEmpty) {
                return const EmptyView(
                  'All clear — no alerts',
                  icon: Icons.check_circle_outline,
                );
              }
              final failed = items.where((r) => r.status == 'FAILED').toList();
              final partial = items.where((r) => r.status == 'PARTIAL').toList();
              return ListView(
                padding: const EdgeInsets.all(16),
                children: [
                  if (failed.isNotEmpty) ...[
                    _SectionHeader(
                      title: 'Failed Executions',
                      count: failed.length,
                      color: AppColors.statusFailed,
                      icon: Icons.error_outline,
                    ),
                    const SizedBox(height: 8),
                    ...failed.map((r) => _AlertTile(item: r)),
                    const SizedBox(height: 20),
                  ],
                  if (partial.isNotEmpty) ...[
                    _SectionHeader(
                      title: 'Partial Executions',
                      count: partial.length,
                      color: AppColors.statusPartial,
                      icon: Icons.warning_amber_outlined,
                    ),
                    const SizedBox(height: 8),
                    ...partial.map((r) => _AlertTile(item: r)),
                  ],
                ],
              );
            },
          ),
        ),
      ),
    );
  }
}

class _SectionHeader extends StatelessWidget {
  const _SectionHeader({
    required this.title,
    required this.count,
    required this.color,
    required this.icon,
  });

  final String title;
  final int count;
  final Color color;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Icon(icon, color: color, size: 18),
        const SizedBox(width: 8),
        Text(
          title,
          style: TextStyle(
            fontSize: 15,
            fontWeight: FontWeight.w600,
            color: color,
          ),
        ),
        const SizedBox(width: 8),
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
          decoration: BoxDecoration(
            color: color.withOpacity(0.12),
            borderRadius: BorderRadius.circular(10),
          ),
          child: Text(
            '$count',
            style: TextStyle(fontSize: 11, fontWeight: FontWeight.w700, color: color),
          ),
        ),
      ],
    );
  }
}

class _AlertTile extends StatelessWidget {
  const _AlertTile({required this.item});
  final RequestSummary item;

  @override
  Widget build(BuildContext context) {
    final isFailed = item.status == 'FAILED';
    final color = isFailed ? AppColors.statusFailed : AppColors.statusPartial;
    return Card(
      margin: const EdgeInsets.only(bottom: 8),
      child: ListTile(
        leading: Icon(
          isFailed ? Icons.error_outline : Icons.warning_amber_outlined,
          color: color,
        ),
        title: Text(
          item.requestRef,
          style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13),
        ),
        subtitle: Text(
          '${item.requestedBySapUser} · ${Formatters.age(item.createdAt.toIso8601String())}',
          style: const TextStyle(fontSize: 12, color: AppColors.textSecondary),
        ),
        trailing: const Icon(Icons.chevron_right, color: AppColors.textMuted),
        onTap: () => context.go('/executions'),
      ),
    );
  }
}
