package com.recbook.hellopos.feature.catalog

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedCard
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.input.key.type
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.hilt.lifecycle.viewmodel.compose.hiltViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.recbook.hellopos.core.designsystem.HelloPosTheme
import java.util.UUID

@Composable
fun CatalogScreen(onBack: () -> Unit, viewModel: CatalogViewModel = hiltViewModel()) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    CatalogContent(
        state,
        CatalogActions(
            onSelect = viewModel::select,
            onQueryChange = viewModel::onQueryChange,
            onSubmit = viewModel::submitQuery,
            onOpen = viewModel::open,
            onCloseDetail = viewModel::closeDetail,
            onRefresh = viewModel::refresh,
            onBack = onBack,
        ),
    )
}

class CatalogActions(
    val onSelect: (UUID) -> Unit = {},
    val onQueryChange: (String) -> Unit = {},
    val onSubmit: () -> Unit = {},
    val onOpen: (UUID) -> Unit = {},
    val onCloseDetail: () -> Unit = {},
    val onRefresh: () -> Unit = {},
    val onBack: () -> Unit = {},
)

@Composable
fun CatalogContent(state: CatalogUiState, actions: CatalogActions, modifier: Modifier = Modifier) {
    Column(modifier = modifier.fillMaxSize().padding(24.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            TextButton(onClick = actions.onBack) { Text("Back") }
            Text("Catalog", style = MaterialTheme.typography.headlineSmall, modifier = Modifier.weight(1f))
            state.updated?.let { Text(it, style = MaterialTheme.typography.bodyMedium) }
            OutlinedButton(onClick = actions.onRefresh, enabled = !state.refreshing) { Text(if (state.refreshing) "Refreshing…" else "Refresh now") }
        }
        state.message?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        state.warnings.forEach { Text(it, color = MaterialTheme.colorScheme.tertiary) }

        if (!state.downloaded) {
            Column(modifier = Modifier.fillMaxSize(), verticalArrangement = Arrangement.Center, horizontalAlignment = Alignment.CenterHorizontally) {
                Text("The catalog has not been downloaded to this till yet.", style = MaterialTheme.typography.titleMedium)
                Button(onClick = actions.onRefresh, enabled = !state.refreshing) { Text("Retry") }
            }
            return@Column
        }

        OutlinedTextField(
            value = state.query,
            onValueChange = actions.onQueryChange,
            label = { Text("Search by name, or scan or type a barcode") },
            singleLine = true,
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            keyboardActions = KeyboardActions(onSearch = { actions.onSubmit() }),
            // A scanner acting as a keyboard ends each code with Enter: submit once, on key up, and keep the
            // key down from also triggering the search action.
            modifier = Modifier.fillMaxWidth().onPreviewKeyEvent { event ->
                val enter = event.key == Key.Enter || event.key == Key.NumPadEnter
                if (enter && event.type == KeyEventType.KeyUp) actions.onSubmit()
                enter
            },
        )
        state.lookupMessage?.let { Text(it, color = MaterialTheme.colorScheme.error) }

        if (!state.searching) {
            LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                items(state.categories, key = { it.id }) { category ->
                    FilterChip(selected = category.id == state.selectedCategory, onClick = { actions.onSelect(category.id) }, label = { Text(category.name) })
                }
            }
        }

        Row(horizontalArrangement = Arrangement.spacedBy(24.dp), modifier = Modifier.fillMaxSize()) {
            Column(modifier = Modifier.weight(1f)) {
                if (state.products.isEmpty()) {
                    Text(if (state.searching) "No products match." else "No products in this category.", style = MaterialTheme.typography.bodyMedium)
                }
                LazyColumn {
                    items(state.products, key = { it.id }) { product ->
                        Row(modifier = Modifier.fillMaxWidth().clickable { actions.onOpen(product.id) }.padding(vertical = 12.dp)) {
                            Text(product.name, modifier = Modifier.weight(1f))
                            Text(product.price)
                        }
                        HorizontalDivider()
                    }
                }
            }
            state.detail?.let { ProductDetailPanel(it, actions.onCloseDetail, Modifier.width(360.dp).fillMaxHeight()) }
        }
    }
}

@Composable
private fun ProductDetailPanel(detail: ProductDetail, onClose: () -> Unit, modifier: Modifier = Modifier) {
    OutlinedCard(modifier = modifier) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(detail.name, style = MaterialTheme.typography.titleLarge, modifier = Modifier.weight(1f))
                TextButton(onClick = onClose) { Text("Close") }
            }
            if (detail.discountedPrice != null) {
                Text(detail.price, textDecoration = TextDecoration.LineThrough)
                Text(detail.discountedPrice, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
                detail.discount?.let { Text(it, color = MaterialTheme.colorScheme.tertiary) }
            } else {
                Text(detail.price, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
            }
            Text(detail.quantity)
            Text(detail.tax)
            detail.ageRestriction?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            detail.deposit?.let { Text(it) }
            if (detail.barcodes.isNotEmpty()) Text("Codes: ${detail.barcodes.joinToString(", ")}", style = MaterialTheme.typography.bodySmall)
        }
    }
}

@Preview(widthDp = 1100, heightDp = 640)
@Composable
private fun CatalogContentPreview() {
    val drinks = CategoryItem(UUID.randomUUID(), "Drinks")
    val cola = ProductItem(UUID.randomUUID(), "Cola 355 ml", "1.99")
    HelloPosTheme {
        CatalogContent(
            CatalogUiState(
                downloaded = true,
                updated = "Catalog updated 5 minutes ago",
                categories = listOf(drinks, CategoryItem(UUID.randomUUID(), "Produce")),
                selectedCategory = drinks.id,
                products = listOf(cola, ProductItem(UUID.randomUUID(), "Bananas", "1.49 / kg")),
                detail = ProductDetail(
                    cola.id, cola.name, "1.99", "1.79", "10% off until 20 Oct 2026", "Sold per piece, from 1 in steps of 1", "HST 13%",
                    deposit = "Container deposit 0.10 per piece", barcodes = listOf("0067000004568"),
                ),
            ),
            CatalogActions(),
        )
    }
}
