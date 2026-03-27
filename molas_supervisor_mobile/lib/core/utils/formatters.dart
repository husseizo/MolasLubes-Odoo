import 'package:intl/intl.dart';

/// Formatting utilities shared across the app.
abstract final class Formatters {
  static final DateFormat _dateFmt = DateFormat('dd MMM yyyy');
  static final DateFormat _dateTimeFmt = DateFormat('dd MMM yyyy HH:mm');
  static final DateFormat _timeFmt = DateFormat('HH:mm');
  static final NumberFormat _qtyFmt = NumberFormat('#,##0.##');
  static final NumberFormat _pctFmt = NumberFormat('0.0%');

  // ---------------------------------------------------------------------------
  // Date/time
  // ---------------------------------------------------------------------------

  /// Returns e.g. `"27 Mar 2026"`.
  static String date(String? iso) {
    if (iso == null) return '—';
    try {
      return _dateFmt.format(DateTime.parse(iso).toLocal());
    } catch (_) {
      return iso;
    }
  }

  /// Returns e.g. `"27 Mar 2026 14:32"`.
  static String dateTime(String? iso) {
    if (iso == null) return '—';
    try {
      return _dateTimeFmt.format(DateTime.parse(iso).toLocal());
    } catch (_) {
      return iso;
    }
  }

  /// Returns e.g. `"14:32"`.
  static String time(String? iso) {
    if (iso == null) return '—';
    try {
      return _timeFmt.format(DateTime.parse(iso).toLocal());
    } catch (_) {
      return iso;
    }
  }

  /// Human-readable age: `"2h ago"`, `"3d ago"`, etc.
  static String age(String? iso) {
    if (iso == null) return '—';
    try {
      final parsed = DateTime.parse(iso).toLocal();
      final diff = DateTime.now().difference(parsed);
      if (diff.inMinutes < 60) return '${diff.inMinutes}m ago';
      if (diff.inHours < 24) return '${diff.inHours}h ago';
      return '${diff.inDays}d ago';
    } catch (_) {
      return iso;
    }
  }

  /// Whether the given ISO timestamp is older than [hours] hours.
  static bool isOlderThan(String? iso, {required int hours}) {
    if (iso == null) return false;
    try {
      final parsed = DateTime.parse(iso).toLocal();
      return DateTime.now().difference(parsed).inHours >= hours;
    } catch (_) {
      return false;
    }
  }

  // ---------------------------------------------------------------------------
  // Numbers
  // ---------------------------------------------------------------------------

  /// Returns e.g. `"1,250.5"` or `"0"`.
  static String qty(double? value) {
    if (value == null) return '—';
    return _qtyFmt.format(value);
  }

  /// Returns e.g. `"87.3%"`.
  static String pct(double? value) {
    if (value == null) return '—';
    return _pctFmt.format(value);
  }

  // ---------------------------------------------------------------------------
  // Strings
  // ---------------------------------------------------------------------------

  /// Converts `PENDING_APPROVAL` → `"Pending Approval"`.
  static String titleCase(String value) {
    return value
        .split('_')
        .map((w) => w.isEmpty
            ? w
            : '${w[0].toUpperCase()}${w.substring(1).toLowerCase()}')
        .join(' ');
  }
}
