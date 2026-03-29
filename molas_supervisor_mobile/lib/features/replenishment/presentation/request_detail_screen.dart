import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:molas_supervisor_mobile/app/theme/colors.dart';
import 'package:molas_supervisor_mobile/core/utils/formatters.dart';
import 'package:molas_supervisor_mobile/features/replenishment/data/models/request_detail.dart';
import 'package:molas_supervisor_mobile/features/replenishment/providers.dart';
import 'package:molas_supervisor_mobile/shared/widgets/error_view.dart';
import 'package:molas_supervisor_mobile/shared/widgets/loading_view.dart';
import 'widgets/line_tile.dart';
import 'widgets/status_chip.dart';

class RequestDetailScreen extends ConsumerStatefulWidget {
  const RequestDetailScreen({super.key, required this.requestRef});
  final String requestRef;

  @override
  ConsumerState<RequestDetailScreen> createState() => _RequestDetailScreenState();
}

class _RequestDetailScreenState extends ConsumerState<RequestDetailScreen> {
  bool _actionLoading = false;

  Future<void> _showApproveDialog(RequestDetail detail) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Approve Request'),
        content: Text('Approve all ${detail.lineCount} lines in ${detail.requestRef}?'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Approve')),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;
    setState(() => _actionLoading = true);
    try {
      final repo = ref.read(replenishmentRepositoryProvider);
      await repo.approve(widget.requestRef);
      ref.invalidate(detailProvider(widget.requestRef));
      ref.invalidate(pendingRequestsProvider);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Request approved'), backgroundColor: AppColors.statusApproved),
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
      if (mounted) setState(() => _actionLoading = false);
    }
  }

  Future<void> _showRejectDialog() async {
    final reasonCtrl = TextEditingController();
    final reason = await showDialog<String>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Reject Request'),
        content: TextField(
          controller: reasonCtrl,
          decoration: const InputDecoration(
            labelText: 'Rejection reason',
            border: OutlineInputBorder(),
          ),
          maxLines: 3,
          autofocus: true,
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('Cancel')),
          FilledButton(
            style: FilledButton.styleFrom(backgroundColor: AppColors.statusRejected),
            onPressed: () {
              final r = reasonCtrl.text.trim();
              if (r.isEmpty) return;
              Navigator.pop(ctx, r);
            },
            child: const Text('Reject'),
          ),
        ],
      ),
    );
    reasonCtrl.dispose();
    if (reason == null || !mounted) return;
    setState(() => _actionLoading = true);
    try {
      final repo = ref.read(replenishmentRepositoryProvider);
      await repo.reject(widget.requestRef, reason);
      ref.invalidate(detailProvider(widget.requestRef));
      ref.invalidate(pendingRequestsProvider);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Request rejected'), backgroundColor: AppColors.statusRejected),
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
      if (mounted) setState(() => _actionLoading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final asyncDetail = ref.watch(detailProvider(widget.requestRef));

    return Scaffold(
      appBar: AppBar(
        leading: BackButton(onPressed: () => context.go('/replenishment/inbox')),
        title: Text(widget.requestRef),
        actions: [
          if (asyncDetail.valueOrNull != null)
            Padding(
              padding: const EdgeInsets.only(right: 12),
              child: StatusChip(asyncDetail.value!.status),
            ),
        ],
      ),
      body: asyncDetail.when(
        loading: () => const LoadingView(),
        error: (err, _) => ErrorView(
          message: err.toString().replaceFirst('Exception: ', ''),
          onRetry: () => ref.invalidate(detailProvider(widget.requestRef)),
        ),
        data: (detail) => _DetailBody(detail: detail),
      ),
      bottomNavigationBar: asyncDetail.whenOrNull(
        data: (detail) {
          if (detail.status != 'PENDING_APPROVAL') return null;
          return _ActionBar(
            loading: _actionLoading,
            onApprove: () => _showApproveDialog(detail),
            onReject: _showRejectDialog,
          );
        },
      ),
    );
  }
}

class _DetailBody extends StatelessWidget {
  const _DetailBody({required this.detail});
  final RequestDetail detail;

  @override
  Widget build(BuildContext context) {
    return ListView(
      children: [
        // Header info
        Padding(
          padding: const EdgeInsets.all(16),
          child: Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  _InfoRow('Requested by', detail.requestedBySapUser),
                  _InfoRow('Created', Formatters.dateTime(detail.createdAt.toIso8601String())),
                  if (detail.sourceWarehouse != null)
                    _InfoRow('Source Warehouse', detail.sourceWarehouse!),
                  if (detail.targetWarehouse != null)
                    _InfoRow('Target Warehouse', detail.targetWarehouse!),
                  if (detail.submittedAt != null)
                    _InfoRow('Submitted', Formatters.dateTime(detail.submittedAt)),
                  if (detail.approvedAt != null)
                    _InfoRow('Approved', Formatters.dateTime(detail.approvedAt)),
                  if (detail.rejectedAt != null)
                    _InfoRow('Rejected', Formatters.dateTime(detail.rejectedAt)),
                  if (detail.rejectionReason != null)
                    _InfoRow('Rejection Reason', detail.rejectionReason!),
                  if (detail.executedAt != null)
                    _InfoRow('Executed', Formatters.dateTime(detail.executedAt)),
                  if (detail.transferRef != null)
                    _InfoRow('Transfer Ref', detail.transferRef!),
                  if (detail.goodsIssueDocNum != null)
                    _InfoRow('GI Doc#', detail.goodsIssueDocNum.toString()),
                  if (detail.goodsReceiptDocNum != null)
                    _InfoRow('GR Doc#', detail.goodsReceiptDocNum.toString()),
                ],
              ),
            ),
          ),
        ),
        // Lines
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: Text(
            'Lines (${detail.lines.length})',
            style: const TextStyle(
              fontSize: 15,
              fontWeight: FontWeight.w600,
              color: AppColors.textPrimary,
            ),
          ),
        ),
        const SizedBox(height: 8),
        ...detail.lines.map((l) => LineTile(line: l)),
        const SizedBox(height: 16),
        // Status Timeline
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: Text(
            'Timeline',
            style: const TextStyle(
              fontSize: 15,
              fontWeight: FontWeight.w600,
              color: AppColors.textPrimary,
            ),
          ),
        ),
        const SizedBox(height: 8),
        _StatusTimeline(detail: detail),
        const SizedBox(height: 32),
      ],
    );
  }
}

class _InfoRow extends StatelessWidget {
  const _InfoRow(this.label, this.value);
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 130,
            child: Text(
              label,
              style: const TextStyle(fontSize: 12, color: AppColors.textSecondary),
            ),
          ),
          Expanded(
            child: Text(
              value,
              style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w500),
            ),
          ),
        ],
      ),
    );
  }
}

class _StatusTimeline extends StatelessWidget {
  const _StatusTimeline({required this.detail});
  final RequestDetail detail;

  @override
  Widget build(BuildContext context) {
    final steps = <_TimelineStep>[
      _TimelineStep(
        label: 'Created',
        time: Formatters.dateTime(detail.createdAt.toIso8601String()),
        done: true,
        icon: Icons.add_circle_outline,
      ),
      _TimelineStep(
        label: 'Submitted',
        time: Formatters.dateTime(detail.submittedAt),
        done: detail.submittedAt != null,
        icon: Icons.send_outlined,
      ),
      if (detail.status == 'REJECTED')
        _TimelineStep(
          label: 'Rejected',
          time: Formatters.dateTime(detail.rejectedAt),
          done: detail.rejectedAt != null,
          icon: Icons.cancel_outlined,
          color: AppColors.statusRejected,
        )
      else ...[
        _TimelineStep(
          label: 'Approved',
          time: Formatters.dateTime(detail.approvedAt),
          done: detail.approvedAt != null,
          icon: Icons.check_circle_outline,
          color: AppColors.statusApproved,
        ),
        _TimelineStep(
          label: 'Executed',
          time: Formatters.dateTime(detail.executedAt),
          done: detail.executedAt != null,
          icon: Icons.play_circle_outline,
          color: AppColors.statusExecuted,
        ),
      ],
    ];

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16),
      child: Column(
        children: steps
            .map(
              (s) => Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Column(
                    children: [
                      Icon(s.icon, color: s.done ? (s.color ?? AppColors.primary) : AppColors.textMuted, size: 20),
                      if (steps.last != s)
                        Container(width: 2, height: 28, color: AppColors.border),
                    ],
                  ),
                  const SizedBox(width: 12),
                  Padding(
                    padding: const EdgeInsets.only(top: 1),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          s.label,
                          style: TextStyle(
                            fontSize: 13,
                            fontWeight: FontWeight.w600,
                            color: s.done ? AppColors.textPrimary : AppColors.textMuted,
                          ),
                        ),
                        Text(
                          s.time,
                          style: const TextStyle(fontSize: 11, color: AppColors.textMuted),
                        ),
                        const SizedBox(height: 12),
                      ],
                    ),
                  ),
                ],
              ),
            )
            .toList(),
      ),
    );
  }
}

class _TimelineStep {
  const _TimelineStep({
    required this.label,
    required this.time,
    required this.done,
    required this.icon,
    this.color,
  });
  final String label;
  final String time;
  final bool done;
  final IconData icon;
  final Color? color;
}

class _ActionBar extends StatelessWidget {
  const _ActionBar({
    required this.loading,
    required this.onApprove,
    required this.onReject,
  });

  final bool loading;
  final VoidCallback onApprove;
  final VoidCallback onReject;

  @override
  Widget build(BuildContext context) {
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
        child: Row(
          children: [
            Expanded(
              child: OutlinedButton.icon(
                onPressed: loading ? null : onReject,
                icon: const Icon(Icons.close, color: AppColors.statusRejected),
                label: const Text('Reject', style: TextStyle(color: AppColors.statusRejected)),
                style: OutlinedButton.styleFrom(
                  side: const BorderSide(color: AppColors.statusRejected),
                  minimumSize: const Size(0, 44),
                ),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: FilledButton.icon(
                onPressed: loading ? null : onApprove,
                icon: loading
                    ? const SizedBox(
                        width: 16,
                        height: 16,
                        child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                      )
                    : const Icon(Icons.check),
                label: const Text('Approve'),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
