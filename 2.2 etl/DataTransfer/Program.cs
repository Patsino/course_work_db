using System;
using System.Collections.Generic;
using System.Data;
using Dapper;
using Npgsql;
using static ETLProcess.Program;

namespace ETLProcess
{
    class Program
    {
        static void Main(string[] args)
        {
            string oltpConnectionString = "Host=localhost;Username=postgres;Password=123;Database=course_Work_A; Include Error Detail=True";
            string olapConnectionString = "Host=localhost;Username=postgres;Password=123;Database=course_work_A_DWH; Include Error Detail=True";

            // Step 1: Extract data from OLTP
            var users = ExtractUsers(oltpConnectionString);
            var books = ExtractBooks(oltpConnectionString);
            var orders = ExtractOrders(oltpConnectionString);
            var orderItems = ExtractOrderItems(oltpConnectionString);
            var shoppingCarts = ExtractShoppingCarts(oltpConnectionString);
            var cartItems = ExtractCartItems(oltpConnectionString);
            var reviews = ExtractReviews(oltpConnectionString);

            // Step 2: Transform data (if needed)
            // Example: Add a transformation function if needed

            // Step 3: Load data into OLAP
            LoadUserDimension(olapConnectionString, users);
            LoadProductDimension(olapConnectionString, books);
            LoadTimeDimension(olapConnectionString, orders);
            LoadLocationDimension(olapConnectionString, orders);
            LoadSalesFact(olapConnectionString, orderItems, orders);
            LoadInventoryFact(olapConnectionString, orderItems, orders);


            Console.WriteLine("ETL process completed successfully.");
        }


        static List<User> ExtractUsers(string connectionString)
        {
            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();
                return connection.Query<User>("SELECT * FROM Users").AsList();
            }
        }

        static List<Book> ExtractBooks(string connectionString)
        {
            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();
                return connection.Query<Book>("SELECT * FROM Books").AsList();
            }
        }

        static List<OrderItem> ExtractOrderItems(string connectionString)
        {
            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();
                var query = @"
            SELECT 
                order_item_id,
                order_id,
                book_id,
                quantity,
                price 
            FROM 
                Order_Items";
                return connection.Query<OrderItem>(query).AsList();
            }
        }

        static List<ShoppingCart> ExtractShoppingCarts(string connectionString)
        {
            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();
                var query = @"
            SELECT 
                cart_id,
                user_id,
                created_at
            FROM 
                Shopping_Cart";
                return connection.Query<ShoppingCart>(query).AsList();
            }
        }


        static List<CartItem> ExtractCartItems(string connectionString)
        {
            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();
                var query = @"
            SELECT 
                cart_item_id,
                cart_id,
                book_id,
                quantity
            FROM 
                Cart_Items";
                return connection.Query<CartItem>(query).AsList();
            }
        }

        static List<Order> ExtractOrders(string connectionString)
        {
            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();
                string query = @"
                    SELECT 
                        o.order_id AS Order_ID, 
                        oi.order_item_id AS Product_ID, 
                        o.user_id AS Customer_ID, 
                        o.order_date AS CreatedAt, 
                        oi.quantity AS Order_Quantity, 
                        oi.price * oi.quantity AS Order_Total 
                    FROM 
                        orders o
                    JOIN
                        order_items oi ON o.order_id = oi.order_id";
                return connection.Query<Order>(query).AsList();
            }
        }

        static List<Review> ExtractReviews(string connectionString)
        {
            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();
                var query = @"
            SELECT 
                review_id,
                user_id,
                book_id,
                rating,
                comment,
                review_date
            FROM 
                Reviews";
                return connection.Query<Review>(query).AsList();
            }
        }


        static void LoadUserDimension(string connectionString, List<User> users)
        {
            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();
                foreach (var user in users)
                {
                    // Check if user already exists in OLAP
                    var existingUser = connection.QuerySingleOrDefault<User>("SELECT * FROM Dim_User WHERE user_id = @UserId", new { UserId = user.UserId });
                    if (existingUser == null)
                    {
                        connection.Execute("INSERT INTO Dim_User (user_id, username, email) VALUES (@UserId, @Username, @Email)", new { user.UserId, user.Username, user.Email });
                    }
                }
            }
        }

        static void LoadProductDimension(string connectionString, List<Book> books)
        {
            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();
                foreach (var book in books)
                {
                    // Check if product already exists in OLAP
                    var existingProduct = connection.QuerySingleOrDefault<ProductDimension>("SELECT * FROM Product_Dimension WHERE product_id = @ProductId", new { ProductId = book.BookId });

                    if (existingProduct == null)
                    {
                        connection.Execute(@"
                            INSERT INTO Product_Dimension (product_id, product_name, category_id, manufacturer_id, price, description) 
                            VALUES (@ProductId, @ProductName, @CategoryId, @ManufacturerId, @Price, @Description)",
                            new
                            {
                                ProductId = book.BookId,
                                ProductName = book.Title,
                                CategoryId = 1, // Assuming a default category id
                                ManufacturerId = 1, // Assuming a default manufacturer id
                                Price = book.Price,
                                Description = book.Description
                            });
                    }
                }
            }
        }

        static void LoadTimeDimension(string connectionString, List<Order> orders)
        {
            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();
                foreach (var order in orders)
                {
                    var date = order.OrderDate.Date;
                    var year = date.Year;
                    var month = date.Month;
                    var quarter = (date.Month - 1) / 3 + 1;
                    var dayOfWeek = (int)date.DayOfWeek;

                    // Check if time dimension already exists in OLAP
                    //var existingTime = connection.QuerySingleOrDefault<TimeDimension>("SELECT * FROM Time_Dimension WHERE date = @Date", new { Date = date });

                    //if (existingTime == null)
                    //{
                        connection.Execute(@"
                            INSERT INTO Time_Dimension (year, month, quarter, day_of_week) 
                            VALUES (@Year, @Month, @Quarter, @DayOfWeek)",
                            new
                            {
                                Year = year,
                                Month = month,
                                Quarter = quarter,
                                DayOfWeek = dayOfWeek
                            });
                    //}
                }
            }
        }

        static void LoadSalesFact(string connectionString, List<OrderItem> orderItems, List<Order> orders)
        {
            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();
                foreach (var orderItem in orderItems)
                {
                    var order = orders.Find(o => o.OrderId == orderItem.OrderId);
                    var timeDim = connection.QuerySingleOrDefault<TimeDimension>("SELECT * FROM Time_Dimension WHERE a_date = @Date", new { Date = order.OrderDate.Date });

                    var existingSalesFact = connection.QuerySingleOrDefault<SalesFact>("SELECT * FROM Sales_Fact WHERE sales_id = @SalesId", new { SalesId = orderItem.OrderItemId });
                    var i = 1;
                    //if (existingSalesFact == null && timeDim != null)
                    //{
                        connection.Execute(@"
                            INSERT INTO Sales_Fact (time_id, product_id, location_id, quantity_sold, amount_sold, discount) 
                            VALUES (@TimeId, @ProductId, @LocationId, @QuantitySold, @AmountSold, @Discount)",
                            new
                            {
                                TimeId = i++,
                                ProductId = orderItem.BookId,
                                LocationId = 2, // Assuming a default location id
                                QuantitySold = orderItem.Quantity,
                                AmountSold = orderItem.Quantity * orderItem.Price,
                                Discount = 0 // Assuming no discount
                            });
                    //}
                }
            }
        }

        static void LoadInventoryFact(string olapConnectionString, List<OrderItem> orderItems, List<Order> orders)
        {
            using (var connection = new NpgsqlConnection(olapConnectionString))
            {
                connection.Open();
                foreach (var orderItem in orderItems)
                {
                    // Get time_id if it exists, otherwise use a temporary value (e.g., 0)
                    //var timeId = connection.QuerySingleOrDefault<int>("SELECT COALESCE((SELECT time_id FROM Time_Dimension WHERE a_date = @orderDate), 0)", new { orderDate = DateTime.Now });
                    // DateTime timeId = DateTime.Now;
                    var i = 1;
                    var order = orders.Find(o => o.OrderId == orderItem.OrderId);
                    var timeDim = i++;


                    // Get product_id
                    var productId = connection.QuerySingleOrDefault<int>("SELECT product_id FROM Product_Dimension WHERE product_id = @ProductId", new { ProductId = orderItem.BookId });

                    // Check if inventory record already exists
                    //var existingInventory = connection.QuerySingleOrDefault<int>("SELECT inventory_id FROM Inventory_Fact WHERE time_id = @timeId AND product_id = @productId", new { timeId, productId });
                    //if (existingInventory == 0)
                    //{
                        // Insert new inventory record
                        connection.Execute("INSERT INTO Inventory_Fact (time_id, product_id, location_id, quantity_in_stock, quantity_sold) VALUES (@timeId, @productId, @locationId, @quantityInStock, @quantitySold)",
                            new
                            {
                                TimeId = timeDim,
                                productId,
                                locationId = 2, // Assuming a default location id
                                quantityInStock = orderItem.Quantity,
                                quantitySold = orderItem.Quantity
                            });
                   //}
                    //else
                    //{
                    //    // Update existing inventory record
                    //    connection.Execute("UPDATE Inventory_Fact SET quantity_in_stock = quantity_in_stock + @quantityInStock, quantity_sold = quantity_sold + @quantitySold WHERE time_id = @timeId AND product_id = @productId",
                    //        new
                    //        {
                    //            timeId,
                    //            productId,
                    //            quantityInStock = orderItem.Quantity,
                    //            quantitySold = orderItem.Quantity
                    //        });
                    //}
                }
            }
        }

        static void LoadLocationDimension(string connectionString, List<Order> orders)
        {
            Random rand = new Random();
            List<string> cities = new List<string>() { "New York", "London", "Tokyo", "Paris", "Berlin" };
            List<string> countries = new List<string>() { "USA", "UK", "Japan", "France", "Germany" };

            using (var connection = new NpgsqlConnection(connectionString))
            {
                connection.Open();
                foreach (var order in orders)
                {
                    // Select random city and country from the predefined lists
                    var city = cities[rand.Next(cities.Count)];
                    var country = countries[rand.Next(countries.Count)];

                    // Insert random location record into Location_Dimension
                    connection.Execute(@"
                INSERT INTO Location_Dimension (city, country) 
                VALUES (@City, @Country)",
                        new
                        {
                            City = city,
                            Country = country
                        });
                }
            }
        }

        public class User
        {
            public int UserId { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
            public string Email { get; set; }
            public string Role { get; set; }
        }

        public class Book
        {
            public int BookId { get; set; }
            public string Title { get; set; }
            public string ISBN { get; set; }
            public decimal Price { get; set; }
            public int Stock { get; set; }
            public string Description { get; set; }
        }

        public class Category
        {
            public int CategoryId { get; set; }
            public string CategoryName { get; set; }
        }

        public class Author
        {
            public int AuthorId { get; set; }
            public string Name { get; set; }
        }

        public class Order
        {
            public int OrderId { get; set; }
            public int UserId { get; set; }
            public DateTime OrderDate { get; set; }
            public string Status { get; set; }
            public decimal TotalAmount { get; set; }
        }

        public class OrderItem
        {
            public int OrderItemId { get; set; }
            public int OrderId { get; set; }
            public int BookId { get; set; }
            public int Quantity { get; set; }
            public decimal Price { get; set; }
        }

        public class ShoppingCart
        {
            public int CartId { get; set; }
            public int UserId { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public class CartItem
        {
            public int CartItemId { get; set; }
            public int CartId { get; set; }
            public int BookId { get; set; }
            public int Quantity { get; set; }
        }

        public class Review
        {
            public int ReviewId { get; set; }
            public int UserId { get; set; }
            public int BookId { get; set; }
            public int Rating { get; set; }
            public string Comment { get; set; }
            public DateTime ReviewDate { get; set; }
        }

        public class TimeDimension
        {
            public int TimeId { get; set; }
            public DateTime Date { get; set; }
            public int Year { get; set; }
            public int Month { get; set; }
            public int Quarter { get; set; }
            public int DayOfWeek { get; set; }
        }

        public class ProductDimension
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public int CategoryId { get; set; }
            public int ManufacturerId { get; set; }
            public decimal Price { get; set; }
            public string Description { get; set; }
        }

        public class LocationDimension
        {
            public int LocationId { get; set; }
            public string City { get; set; }
            public string Country { get; set; }
        }

        public class SalesFact
        {
            public int SalesId { get; set; }
            public int TimeId { get; set; }
            public int ProductId { get; set; }
            public int LocationId { get; set; }
            public int QuantitySold { get; set; }
            public decimal AmountSold { get; set; }
            public decimal Discount { get; set; }
        }

        public class InventoryFact
        {
            public int InventoryId { get; set; }
            public int TimeId { get; set; }
            public int ProductId { get; set; }
            public int LocationId { get; set; }
            public int QuantityInStock { get; set; }
            public int QuantitySold { get; set; }
        }
    }
}