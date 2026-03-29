import 'package:flutter/material.dart';
import 'package:molas_supervisor_mobile/app/theme/colors.dart';
import 'package:molas_supervisor_mobile/core/utils/formatters.dart';
import 'package:molas_supervisor_mobile/features/replenishment/data/models/request_summary.dart';
import 'status_chip.dart';

class RequestCard extends StatelessWidget {
  const RequestCard({
    super.key,
    required this.summary,
    required this.onTap,
  });

  final RequestSummary summary;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final age = Formatters.age(summary.createdAt.toIso8601String());
    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(8),
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      summary.requestRef,
                      style: const TextStyle(
                        fontWeight: FontWeight.w600,
                        fontSize: 14,
                        color: AppColors.textPrimary,
                      ),
                    ),
                  ),
                  StatusChip(summary.status),
                ],
              ),
              const SizedBox(height: 8),
              Row(
                children: [
                  const Icon(Icons.person_outline, size: 14, color: AppColors.textSecondary),
                  const SizedBox(width: 4),
                  Text(
                    summary.requestedBySapUser,
                    style: const TextStyle(
                      fontSize: 13,
                      color: AppColors.textSecondary,
                    ),
                  ),
                  const Spacer(),
                  const Icon(Icons.list_alt_outlined, size: 14, color: AppColors.textSecondary),
                  const SizedBox(width: 4),
                  Text(
                    '${summary.lineCount} lines',
                    style: const TextStyle(
                      fontSize: 13,
                      color: AppColors.textSecondary,
                    ),
                  ),
                ],
              ),
              if (summary.sourceWarehouse != null || summary.targetWarehouse != null) ...[
                const SizedBox(height: 6),
                Row(
                  children: [
                    const Icon(Icons.warehouse_outlined, size: 14, color: AppColors.textMuted),
                    const SizedBox(width: 4),
                    Text(
                      '${summary.sourceWarehouse ?? '?'} → ${summary.targetWarehouse ?? '?'}',
                      style: const TextStyle(fontSize: 12, color: AppColors.textMuted),
                    ),
                    const Spacer(),
                    Text(
                      age,
                      style: const TextStyle(fontSize: 12, color: AppColors.textMuted),
                    ),
                  ],
                ),
              ] else ...[
                const SizedBox(height: 4),
                Align(
                  alignment: Alignment.centerRight,
                  child: Text(
                    age,
                    style: const TextStyle(fontSize: 12, color: AppColors.textMuted),
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
