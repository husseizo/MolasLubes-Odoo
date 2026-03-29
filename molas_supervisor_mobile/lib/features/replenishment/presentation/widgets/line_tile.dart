import 'package:flutter/material.dart';
import 'package:molas_supervisor_mobile/app/theme/colors.dart';
import 'package:molas_supervisor_mobile/core/utils/formatters.dart';
import 'package:molas_supervisor_mobile/features/replenishment/data/models/replenishment_line.dart';
import 'status_chip.dart';

class LineTile extends StatelessWidget {
  const LineTile({super.key, required this.line});

  final ReplenishmentLine line;

  String _execStatus(LineExecutionStatus s) => switch (s) {
        LineExecutionStatus.pending => 'PENDING',
        LineExecutionStatus.executed => 'EXECUTED',
        LineExecutionStatus.giIssued => 'GI_ISSUED',
        LineExecutionStatus.failed => 'FAILED',
      };

  @override
  Widget build(BuildContext context) {
    return ListTile(
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
      title: Row(
        children: [
          Expanded(
            child: Text(
              line.articleNumber,
              style: const TextStyle(
                fontWeight: FontWeight.w600,
                fontSize: 13,
                color: AppColors.textPrimary,
              ),
            ),
          ),
          StatusChip(_execStatus(line.executionStatus)),
        ],
      ),
      subtitle: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const SizedBox(height: 2),
          Text(
            line.itemDescription,
            style: const TextStyle(fontSize: 12, color: AppColors.textSecondary),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
          ),
          const SizedBox(height: 4),
          Row(
            children: [
              Text(
                'Suggested: ${Formatters.qty(line.suggestedQty)}',
                style: const TextStyle(fontSize: 12, color: AppColors.textMuted),
              ),
              if (line.approvedQty != null) ...[
                const SizedBox(width: 12),
                Text(
                  'Approved: ${Formatters.qty(line.approvedQty)}',
                  style: const TextStyle(
                    fontSize: 12,
                    color: AppColors.statusApproved,
                    fontWeight: FontWeight.w500,
                  ),
                ),
              ],
            ],
          ),
        ],
      ),
      isThreeLine: true,
    );
  }
}
