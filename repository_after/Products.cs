using System;
using System.Collections.Generic;
using System.Linq;

namespace RepositoryAfter
{
    public static class ProductFunctions
    {
        private static readonly List<Product> Catalog = new()
        {
            new Product { Name = "Laptop", Category = "Electronics", Price = 1499.99m, IsFeatured = true },
            new Product { Name = "Headphones", Category = "Electronics", Price = 199.99m, IsFeatured = true },
            new Product { Name = "Office Chair", Category = "Furniture", Price = 329.99m, IsFeatured = false },
            new Product { Name = "Standing Desk", Category = "Furniture", Price = 649.99m, IsFeatured = true }
        };

        public static List<Dictionary<string, object>> GetProductsFromMemory()
        {
            return Catalog
                .Select(product => new Dictionary<string, object>
                {
                    ["name"] = product.Name,
                    ["category"] = product.Category,
                    ["price"] = product.Price,
                    ["isFeatured"] = product.IsFeatured
                })
                .ToList();
        }

        public static List<Product> GetFeaturedProductsFromMemory()
        {
            return Catalog
                .Where(product => product.IsFeatured)
                .Select(product => product.Clone())
                .ToList();
        }
    }

    public class Product
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public bool IsFeatured { get; set; }

        public Product Clone()
        {
            return new Product
            {
                Name = Name,
                Category = Category,
                Price = Price,
                IsFeatured = IsFeatured
            };
        }
    }

    public class ProductCatalog
    {
        private readonly IReadOnlyCollection<Product> _products;

        public ProductCatalog()
        {
            _products = ProductFunctions.GetFeaturedProductsFromMemory();
        }

        public List<Product> GetFeaturedProducts()
        {
            return _products
                .Where(product => product.IsFeatured)
                .Select(product => product.Clone())
                .ToList();
        }
    }
}
