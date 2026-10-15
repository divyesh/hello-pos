package com.recbook.hellopos.data.catalog

import android.content.Context
import androidx.room.Dao
import androidx.room.Database
import androidx.room.Entity
import androidx.room.Index
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.PrimaryKey
import androidx.room.Query
import androidx.room.Room
import androidx.room.RoomDatabase
import kotlinx.coroutines.flow.Flow

// The till's catalog copy (feature #13, data-model.md, research R6) with the business settings of feature
// #12. Ids are UUID strings, dates ISO strings, quantities and rates invariant decimal strings, money minor
// units.

/** The single row describing the copy: whose it is, how fresh, the pricing settings and the selling rules. */
@Entity(tableName = "catalog_meta")
data class CatalogMetaEntity(
    @PrimaryKey val id: Int = 0,
    val businessId: String,
    val revision: Long,
    val refreshedAtMillis: Long,
    val serverOffsetMillis: Long,
    val currency: String,
    val pricingMode: String,
    val timeZone: String,
    val cashRoundingStep: Long,
    val depositsTaxable: Boolean,
    val usesTables: Boolean,
    val discountLimitPercent: String,
    val discountLimitAmount: Long?,
    val tipsEnabled: Boolean,
    /** Comma-separated ascending percentages; empty when tips are off. */
    val tipPresets: String,
)

@Entity(tableName = "tax_profiles")
data class TaxProfileEntity(
    @PrimaryKey val id: String,
    val name: String,
    val kind: String,
    val active: Boolean,
)

@Entity(tableName = "tax_versions", indices = [Index("taxProfileId")])
data class TaxVersionEntity(
    @PrimaryKey val id: String,
    val taxProfileId: String,
    val effectiveFrom: String,
    /** Always read whole: `[{"label":"CGST","ratePercent":"2.5"}]`. */
    val componentsJson: String,
)

@Entity(tableName = "categories")
data class CategoryEntity(
    @PrimaryKey val id: String,
    val name: String,
    val sortOrder: Int,
    val defaultTaxProfileId: String,
    val active: Boolean,
)

@Entity(tableName = "products", indices = [Index("categoryId"), Index("searchName")])
data class ProductEntity(
    @PrimaryKey val id: String,
    val name: String,
    /** [com.recbook.hellopos.domain.catalog.SearchText.normalize] of the name. */
    val searchName: String,
    val sku: String?,
    val categoryId: String,
    val taxProfileId: String?,
    val unit: String,
    val unitPrice: Long?,
    val openPrice: Boolean,
    val minQuantity: String,
    val step: String,
    val minAge: Int?,
    val depositPerUnit: Long,
    val active: Boolean,
)

/** The barcode index: one product per normalized code (research R8). */
@Entity(tableName = "barcodes", indices = [Index("productId")])
data class BarcodeEntity(
    @PrimaryKey val code: String,
    val productId: String,
)

@Entity(tableName = "discounts", indices = [Index("productId")])
data class DiscountEntity(
    @PrimaryKey val id: String,
    val productId: String,
    val kind: String,
    val value: String,
    val fromDate: String,
    val toDate: String?,
)

@Entity(tableName = "payment_types")
data class PaymentTypeEntity(
    @PrimaryKey val id: String,
    val name: String,
    val kind: String,
    val builtIn: Boolean,
    val opensDrawer: Boolean,
    val asksReference: Boolean,
    val givesChange: Boolean,
    val position: Int,
    val active: Boolean,
)

@Entity(tableName = "table_areas")
data class TableAreaEntity(
    @PrimaryKey val id: String,
    val name: String,
    val position: Int,
    val active: Boolean,
)

@Entity(tableName = "dining_tables", indices = [Index("areaId")])
data class DiningTableEntity(
    @PrimaryKey val id: String,
    val areaId: String,
    val name: String,
    val seats: Int,
    val position: Int,
    val active: Boolean,
)

@Dao
interface CatalogDao {

    @Query("SELECT * FROM catalog_meta WHERE id = 0")
    fun observeMeta(): Flow<CatalogMetaEntity?>

    @Query("SELECT * FROM catalog_meta WHERE id = 0")
    suspend fun meta(): CatalogMetaEntity?

    @Query("SELECT * FROM categories WHERE active ORDER BY sortOrder, name")
    fun observeActiveCategories(): Flow<List<CategoryEntity>>

    @Query(
        "SELECT p.* FROM products p JOIN categories c ON c.id = p.categoryId " +
            "WHERE p.categoryId = :categoryId AND p.active AND c.active ORDER BY p.name",
    )
    fun observeActiveProducts(categoryId: String): Flow<List<ProductEntity>>

    @Query("SELECT p.* FROM barcodes b JOIN products p ON p.id = b.productId WHERE b.code = :code")
    suspend fun productByCode(code: String): ProductEntity?

    @Query("SELECT * FROM categories WHERE id = :id")
    suspend fun category(id: String): CategoryEntity?

    /** [pattern] is a LIKE pattern on the normalized name, with ! as the escape character. */
    @Query(
        "SELECT p.* FROM products p JOIN categories c ON c.id = p.categoryId " +
            "WHERE p.searchName LIKE :pattern ESCAPE '!' AND p.active AND c.active ORDER BY p.name LIMIT :limit",
    )
    suspend fun searchProducts(pattern: String, limit: Int): List<ProductEntity>

    @Query("SELECT * FROM tax_profiles")
    suspend fun taxProfiles(): List<TaxProfileEntity>

    @Query("SELECT * FROM tax_versions ORDER BY effectiveFrom")
    suspend fun taxVersions(): List<TaxVersionEntity>

    @Query("SELECT * FROM categories ORDER BY sortOrder, name")
    suspend fun categories(): List<CategoryEntity>

    @Query("SELECT * FROM products ORDER BY name")
    suspend fun products(): List<ProductEntity>

    @Query("SELECT * FROM barcodes")
    suspend fun barcodes(): List<BarcodeEntity>

    @Query("SELECT * FROM barcodes WHERE productId IN (:productIds)")
    suspend fun barcodesOf(productIds: List<String>): List<BarcodeEntity>

    @Query("SELECT * FROM discounts")
    suspend fun discounts(): List<DiscountEntity>

    @Query("SELECT * FROM payment_types WHERE active ORDER BY position, name")
    fun observeActivePaymentTypes(): Flow<List<PaymentTypeEntity>>

    @Query("SELECT * FROM payment_types ORDER BY position, name")
    suspend fun paymentTypes(): List<PaymentTypeEntity>

    @Query("SELECT * FROM table_areas ORDER BY position, name")
    suspend fun tableAreas(): List<TableAreaEntity>

    @Query("SELECT * FROM dining_tables ORDER BY areaId, position, name")
    suspend fun diningTables(): List<DiningTableEntity>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertMeta(meta: CatalogMetaEntity)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertTaxProfiles(rows: List<TaxProfileEntity>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertTaxVersions(rows: List<TaxVersionEntity>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertCategories(rows: List<CategoryEntity>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertProducts(rows: List<ProductEntity>)

    /** ABORT: a code on two products is a broken download, and the whole apply rolls back. */
    @Insert(onConflict = OnConflictStrategy.ABORT)
    suspend fun insertBarcodes(rows: List<BarcodeEntity>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertDiscounts(rows: List<DiscountEntity>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertPaymentTypes(rows: List<PaymentTypeEntity>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertTableAreas(rows: List<TableAreaEntity>)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertDiningTables(rows: List<DiningTableEntity>)

    @Query("DELETE FROM tax_versions WHERE taxProfileId IN (:profileIds)")
    suspend fun deleteTaxVersionsOf(profileIds: List<String>)

    @Query("DELETE FROM barcodes WHERE productId IN (:productIds)")
    suspend fun deleteBarcodesOf(productIds: List<String>)

    @Query("DELETE FROM discounts WHERE id IN (:ids)")
    suspend fun deleteDiscounts(ids: List<String>)

    @Query("DELETE FROM catalog_meta")
    suspend fun clearMeta()

    @Query("DELETE FROM tax_profiles")
    suspend fun clearTaxProfiles()

    @Query("DELETE FROM tax_versions")
    suspend fun clearTaxVersions()

    @Query("DELETE FROM categories")
    suspend fun clearCategories()

    @Query("DELETE FROM products")
    suspend fun clearProducts()

    @Query("DELETE FROM barcodes")
    suspend fun clearBarcodes()

    @Query("DELETE FROM discounts")
    suspend fun clearDiscounts()

    @Query("DELETE FROM payment_types")
    suspend fun clearPaymentTypes()

    @Query("DELETE FROM table_areas")
    suspend fun clearTableAreas()

    @Query("DELETE FROM dining_tables")
    suspend fun clearDiningTables()
}

@Database(
    entities = [
        CatalogMetaEntity::class, TaxProfileEntity::class, TaxVersionEntity::class, CategoryEntity::class,
        ProductEntity::class, BarcodeEntity::class, DiscountEntity::class,
        PaymentTypeEntity::class, TableAreaEntity::class, DiningTableEntity::class,
    ],
    version = 2,
    exportSchema = true,
)
abstract class CatalogDatabase : RoomDatabase() {
    abstract fun catalog(): CatalogDao

    companion object {
        const val NAME = "catalog.db"

        /**
         * The copy on disk. A schema change drops it (it is only a copy of the server), so the next refresh
         * finds no revision and downloads everything again (feature #12: version 1 to 2 added the settings).
         */
        fun builder(context: Context, name: String = NAME): RoomDatabase.Builder<CatalogDatabase> =
            Room.databaseBuilder(context, CatalogDatabase::class.java, name).fallbackToDestructiveMigration(dropAllTables = true)
    }
}
