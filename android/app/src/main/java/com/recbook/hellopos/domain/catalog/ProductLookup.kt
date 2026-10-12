package com.recbook.hellopos.domain.catalog

/** The answer to "which product has this code?" on the till (feature #13, FR-010–FR-015). */
sealed interface ProductLookup {
    data class Found(val product: CatalogProduct) : ProductLookup

    /** The code itself is wrong (check digit, length…); [message] says why. */
    data class Invalid(val message: String) : ProductLookup

    /** A valid code that no product has. */
    data object NotFound : ProductLookup

    /** The product exists but is deactivated or in an inactive category; it is kept for sales history only. */
    data class NotOffered(val name: String) : ProductLookup

    companion object {
        const val NOT_FOUND = "No product with this code."
    }
}
