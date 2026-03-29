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

class ExecutionStatusScreen extends ConsumerWidget {
  const ExecutionStatusScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final asyncData = ref.watch(partialFailedProvider);

    return AppScaffold(
      currentIndex: 2,
      child: DefaultTabController(
        length: 2,
        child: Scaffold(
          appBar: AppBar(
            title: const Text('Execution Status'),
            leading: BackButton(onPressed: () => context.go('/home')),
            bottom: const TabBar(
              tabs: [
                Tab(text: 'PARTIAL'),
                Tab(text: 'FAILED'),
              ],
            ),
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
                final partial = items.where((r) => r.status == 'PARTIAL').toList();
                final failed = items.where((r) => r.status == 'FAILED').toList();
                return TabBarView(
                  children: [
                    _ExecutionList(items: partial, emptyMsg: 'No partial executions'),
                    _ExecutionList(items: failed, emptyMsg: 'No failed executions'),
                  ],
                );
              },
            ),
          ),
        ),
      ),
    );
  }
}

class _ExecutionList extends StatelessWidget {
  const _ExecutionList({
    required this.items,
    required this.emptyMsg,
  });

  final List<RequestSummary> items;
  final String emptyMsg;

  @override
  Widget build(BuildContext context) {
    if (items.isEmpty) return EmptyView(emptyMsg);
    return ListView.builder(
      itemCount: items.length,
      padding: const EdgeInsets.symmetric(vertical: 8),
      itemBuilder: (ctx, i) => _ExecutionTile(item: items[i]),
    );
  }
}

class _ExecutionTile extends ConsumerStatefulWidget {
  const _ExecutionTile({required this.item});
  final RequestSummary item;

  @override
  ConsumerState<_ExecutionTile> createState() => _ExecutionTileState();
}

class _ExecutionTileState extends ConsumerState<_ExecutionTile> {
  bool _loading = false;

  Future<void> _retry() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Retry Execution'),
        content: Text('Retry ${widget.item.requestRef}?'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Retry')),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;
    setState(() => _loading = true);
    try {
      final repo = ref.read(executionRepositoryProvider);
      await repo.retry(widget.item.requestRef);
      ref.invalidate(partialFailedProvider);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Retry submitted')),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(e.toString().replaceFirst('Exception: ', '')),
            backgroundColor: Colors.redAccent,
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final item = widget.item;
    final statusColor = item.status == 'FAILED' ? AppColors.statusFailed : AppColors.statusPartial;
    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    item.requestRef,
                    style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14),
                  ),
                  const SizedBox(height: 4),
                  Row(
                    children: [
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                        decoration: BoxDecoration(
                          color: statusColor.withOpacity(0.12),
                          borderRadius: BorderRadius.circular(8),
                        ),
                        child: Text(
                          item.status,
                          style: TextStyle(
                            fontSize: 11,
                            fontWeight: FontWeight.w600,
                            color: statusColor,
                          ),
                        ),
                      ),
                      const SizedBox(width: 8),
                      Text(
                        Formatters.age(item.createdAt.toIso8601String()),
                        style: const TextStyle(fontSize: 12, color: AppColors.textMuted),
                      ),
                    ],
                  ),
                ],
              ),
            ),
            _loading
                ? const SizedBox(
                    width: 24,
                    height: 24,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : IconButton(
                    icon: const Icon(Icons.replay, color: AppColors.primary),
                    onPressed: _retry,
                    tooltip: 'Retry',
                  ),
          ],
        ),
      ),
    );
  }
}
