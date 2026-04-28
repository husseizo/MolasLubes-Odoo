import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:molas_supervisor_mobile/core/providers/providers.dart';

/// Replenishment list screen with status filters and search.
///
/// TODO: Connect to replenishment_repository for real data
class ReplenishmentListScreen extends ConsumerStatefulWidget {
  const ReplenishmentListScreen({super.key});

  @override
  ConsumerState<ReplenishmentListScreen> createState() =>
      _ReplenishmentListScreenState();
}

class _ReplenishmentListScreenState
    extends ConsumerState<ReplenishmentListScreen>
    with SingleTickerProviderStateMixin {
  late TabController _tabController;
  final _searchController = TextEditingController();
  String _searchQuery = '';

  @override
  void initState() {
    super.initState();
    _tabController = TabController(length: 5, vsync: this);
  }

  @override
  void dispose() {
    _tabController.dispose();
    _searchController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final userRole = ref.watch(currentUserRoleProvider);

    return Scaffold(
      appBar: AppBar(
        title: const Text('Replenishments'),
        bottom: TabBar(
          controller: _tabController,
          isScrollable: true,
          tabs: const [
            Tab(text: 'All'),
            Tab(text: 'Pending'),
            Tab(text: 'Approved'),
            Tab(text: 'Rejected'),
            Tab(text: 'Executed'),
          ],
        ),
      ),
      body: Column(
        children: [
          // Search Bar
          Padding(
            padding: const EdgeInsets.all(16),
            child: TextField(
              controller: _searchController,
              decoration: InputDecoration(
                hintText: 'Search by product name or request #',
                prefixIcon: const Icon(Icons.search),
                suffixIcon: _searchQuery.isNotEmpty
                    ? IconButton(
                        icon: const Icon(Icons.clear),
                        onPressed: () {
                          _searchController.clear();
                          setState(() => _searchQuery = '');
                        },
                      )
                    : null,
                border: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(12),
                ),
              ),
              onChanged: (value) => setState(() => _searchQuery = value),
            ),
          ),

          // List
          Expanded(
            child: TabBarView(
              controller: _tabController,
              children: [
                _buildList('All'),
                _buildList('PENDING_APPROVAL'),
                _buildList('APPROVED'),
                _buildList('REJECTED'),
                _buildList('EXECUTED'),
              ],
            ),
          ),
        ],
      ),
      floatingActionButton: _canCreateRequest(userRole)
          ? FloatingActionButton.extended(
              onPressed: () {
                // TODO: Navigate to create replenishment screen
                ScaffoldMessenger.of(context).showSnackBar(
                  const SnackBar(
                    content: Text('Create screen coming soon!'),
                  ),
                );
              },
              icon: const Icon(Icons.add),
              label: const Text('New Request'),
            )
          : null,
    );
  }

  Widget _buildList(String status) {
    // TODO: Replace with real data from replenishment_repository
    final mockData = _getMockReplenishments(status);

    if (mockData.isEmpty) {
      return Center(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(
              Icons.inbox_outlined,
              size: 64,
              color: Colors.grey[400],
            ),
            const SizedBox(height: 16),
            Text(
              'No replenishments found',
              style: Theme.of(context).textTheme.titleMedium?.copyWith(
                    color: Colors.grey[600],
                  ),
            ),
          ],
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: () => _refreshList(status),
      child: ListView.builder(
        padding: const EdgeInsets.symmetric(horizontal: 16),
        itemCount: mockData.length,
        itemBuilder: (context, index) {
          final item = mockData[index];
          return _ReplenishmentCard(
            requestNumber: item['requestNumber'] as String,
            productName: item['productName'] as String,
            quantity: item['quantity'] as int,
            status: item['status'] as String,
            createdDate: item['createdDate'] as String,
            onTap: () => _navigateToDetail(item['id'] as int),
          );
        },
      ),
    );
  }

  Future<void> _refreshList(String status) async {
    // TODO: Call replenishment_repository to refresh data
    await Future.delayed(const Duration(seconds: 1));
  }

  void _navigateToDetail(int id) {
    // TODO: Navigate to replenishment detail screen
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text('Detail screen for ID: $id coming soon!')),
    );
  }

  bool _canCreateRequest(String? role) {
    return role == 'Planner' ||
        role == 'Supervisor' ||
        role == 'Admin';
  }

  List<Map<String, dynamic>> _getMockReplenishments(String status) {
    // Mock data for demonstration
    final allData = [
      {
        'id': 1,
        'requestNumber': 'REQ-2025-001',
        'productName': 'LIQUI MOLY MoS2 Anti-Friction Engine Treatment',
        'quantity': 24,
        'status': 'PENDING_APPROVAL',
        'createdDate': '2025-01-15',
      },
      {
        'id': 2,
        'requestNumber': 'REQ-2025-002',
        'productName': 'LIQUI MOLY Pro-Line Engine Flush',
        'quantity': 12,
        'status': 'APPROVED',
        'createdDate': '2025-01-14',
      },
      {
        'id': 3,
        'requestNumber': 'REQ-2025-003',
        'productName': 'LIQUI MOLY Diesel Smoke Stop',
        'quantity': 36,
        'status': 'PENDING_APPROVAL',
        'createdDate': '2025-01-13',
      },
      {
        'id': 4,
        'requestNumber': 'REQ-2025-004',
        'productName': 'LIQUI MOLY Ceratec Premium Ceramic Wear Protection',
        'quantity': 18,
        'status': 'EXECUTED',
        'createdDate': '2025-01-12',
      },
      {
        'id': 5,
        'requestNumber': 'REQ-2025-005',
        'productName': 'LIQUI MOLY Oil Sludge Flush',
        'quantity': 8,
        'status': 'REJECTED',
        'createdDate': '2025-01-11',
      },
    ];

    if (status == 'All') return allData;
    return allData
        .where((item) => item['status'] == status)
        .toList();
  }
}

class _ReplenishmentCard extends StatelessWidget {
  const _ReplenishmentCard({
    required this.requestNumber,
    required this.productName,
    required this.quantity,
    required this.status,
    required this.createdDate,
    required this.onTap,
  });

  final String requestNumber;
  final String productName;
  final int quantity;
  final String status;
  final String createdDate;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final statusColor = _getStatusColor(status);
    final statusText = _getStatusText(status);

    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(12),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      requestNumber,
                      style: Theme.of(context).textTheme.titleMedium?.copyWith(
                            fontWeight: FontWeight.bold,
                          ),
                    ),
                  ),
                  Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 8,
                      vertical: 4,
                    ),
                    decoration: BoxDecoration(
                      color: statusColor.withOpacity(0.2),
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: Text(
                      statusText,
                      style: TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w600,
                        color: statusColor,
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 8),
              Text(
                productName,
                style: Theme.of(context).textTheme.bodyMedium,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
              ),
              const SizedBox(height: 12),
              Row(
                children: [
                  Icon(
                    Icons.inventory_2_outlined,
                    size: 16,
                    color: Colors.grey[600],
                  ),
                  const SizedBox(width: 4),
                  Text(
                    'Qty: $quantity',
                    style: Theme.of(context).textTheme.bodySmall?.copyWith(
                          color: Colors.grey[600],
                        ),
                  ),
                  const SizedBox(width: 16),
                  Icon(
                    Icons.calendar_today_outlined,
                    size: 16,
                    color: Colors.grey[600],
                  ),
                  const SizedBox(width: 4),
                  Text(
                    createdDate,
                    style: Theme.of(context).textTheme.bodySmall?.copyWith(
                          color: Colors.grey[600],
                        ),
                  ),
                  const Spacer(),
                  Icon(
                    Icons.arrow_forward_ios,
                    size: 16,
                    color: Colors.grey[400],
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }

  Color _getStatusColor(String status) {
    return switch (status) {
      'PENDING_APPROVAL' => Colors.orange,
      'APPROVED' => Colors.green,
      'REJECTED' => Colors.red,
      'EXECUTED' => Colors.blue,
      _ => Colors.grey,
    };
  }

  String _getStatusText(String status) {
    return switch (status) {
      'PENDING_APPROVAL' => 'Pending',
      'APPROVED' => 'Approved',
      'REJECTED' => 'Rejected',
      'EXECUTED' => 'Executed',
      _ => status,
    };
  }
}
