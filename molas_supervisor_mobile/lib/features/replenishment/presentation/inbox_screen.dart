import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:molas_supervisor_mobile/features/replenishment/providers.dart';
import 'package:molas_supervisor_mobile/shared/widgets/app_scaffold.dart';
import 'package:molas_supervisor_mobile/shared/widgets/empty_view.dart';
import 'package:molas_supervisor_mobile/shared/widgets/error_view.dart';
import 'package:molas_supervisor_mobile/shared/widgets/loading_view.dart';
import 'widgets/request_card.dart';

class InboxScreen extends ConsumerWidget {
  const InboxScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final asyncData = ref.watch(pendingRequestsProvider);

    return AppScaffold(
      currentIndex: 1,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('Approval Inbox'),
          leading: BackButton(onPressed: () => context.go('/home')),
        ),
        body: RefreshIndicator(
          onRefresh: () async => ref.invalidate(pendingRequestsProvider),
          child: asyncData.when(
            loading: () => const LoadingView(),
            error: (err, _) => ErrorView(
              message: err.toString().replaceFirst('Exception: ', ''),
              onRetry: () => ref.invalidate(pendingRequestsProvider),
            ),
            data: (items) {
              if (items.isEmpty) {
                return const EmptyView('No pending approvals');
              }
              return ListView.builder(
                itemCount: items.length,
                padding: const EdgeInsets.symmetric(vertical: 8),
                itemBuilder: (ctx, i) => RequestCard(
                  summary: items[i],
                  onTap: () => context.go('/replenishment/${items[i].requestRef}'),
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}
