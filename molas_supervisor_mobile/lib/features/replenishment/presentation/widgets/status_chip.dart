import 'package:flutter/material.dart';
import 'package:molas_supervisor_mobile/app/theme/colors.dart';
import 'package:molas_supervisor_mobile/core/utils/formatters.dart';

class StatusChip extends StatelessWidget {
  const StatusChip(this.status, {super.key});

  final String status;

  Color _colorFor(String s) {
    return switch (s.toUpperCase()) {
      'PENDING_APPROVAL' || 'PENDING' => AppColors.statusPending,
      'APPROVED' => AppColors.statusApproved,
      'REJECTED' => AppColors.statusRejected,
      'EXECUTED' => AppColors.statusExecuted,
      'FAILED' => AppColors.statusFailed,
      'PARTIAL' => AppColors.statusPartial,
      'DRAFT' => AppColors.statusDraft,
      _ => AppColors.statusDraft,
    };
  }

  @override
  Widget build(BuildContext context) {
    final color = _colorFor(status);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: color.withOpacity(0.12),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: color.withOpacity(0.4)),
      ),
      child: Text(
        Formatters.titleCase(status),
        style: TextStyle(
          color: color,
          fontSize: 11,
          fontWeight: FontWeight.w600,
          letterSpacing: 0.2,
        ),
      ),
    );
  }
}
