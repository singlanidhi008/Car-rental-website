using AutoMapper;
using BusinessAccessLayer.Interfaces;
using BusinessAccessLayer.Services;
using DataAccessLayer.DbContext;
using DataAccessLayer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using System.Security.Claims;

namespace AngularBackend.Controllers
{
    //[Authorize]
    public class CarController : Controller
    {
        private readonly IGenericCrud<CarModel> _carService;
        private readonly AngularDbContext _context;
        private readonly IGenericCrud<BookedCar> _bookcar;
        private readonly ItemService _itemService;
        private object? errorMessage;
        private readonly IGenericCrud<UserTotalRentDto> _userTotalRent;
        private readonly IStripeService _service;

        private IMapper _Mapper { get; }

        public CarController(IGenericCrud<CarModel> carService, AngularDbContext context, IMapper mapper, IGenericCrud<BookedCar> bookcar, ItemService itemService, IGenericCrud<UserTotalRentDto> userTotalRent, IStripeService service)
        {
            _carService = carService;
            _context = context;
            _Mapper = mapper;
            _bookcar = bookcar;
            _itemService = itemService;
            _userTotalRent = userTotalRent;
            _service = service;
        }
        //[Authorize(Roles = "Admin,User")]

        [HttpGet("GetAllCars")]

        public async Task<ActionResult<List<CarModel>>> GetAllCars()
        {
            var result = await _carService.GetAll();
            return Ok(result);
        }
        //[Authorize(Roles = "Admin")]

        [HttpPost("AddCar")]
        public async Task<ActionResult> AddCar([FromBody] AddModel car)
        {
           
            var result = new CarModel()
            {
                Id = Guid.NewGuid(),
                Brand = car.Brand,
                pricePerHour = car.pricePerHour,
                Description = car.Description
            };
            _carService.Insert(result);
            _carService.Save();
            return Ok(result);

        }

        //[Authorize(Roles = "Admin")]

        [HttpGet("AllBookCars")]
        public async Task<ActionResult<List<BookedCar>>> GetAllBookedCars()
        {
            var result = await _bookcar.GetAll();
            return Ok(result);
        }
        //[Authorize(Roles = "Admin,User")]
        [HttpGet("{id}/GetCarById")]
        public async Task<ActionResult<CarModel>> CarById(Guid id)
        {
            var result = await _carService.GetById(id);
            return (result);
        }

        //[Authorize(Roles = "User")]
        [HttpPost("{carId}/book")]
        public async Task<ActionResult> BookCar(Guid carId, [FromBody] BookedCarDto bookcar)
        {
            var username = User.Identity.Name;
            var car = await _context.cars.FindAsync(carId);
            if (car == null)
            {
                return NotFound("Car not found.");
            }

            bool isCarAvailable = await _itemService.IsCarAvailable(carId, bookcar.From, bookcar.To);
            if (!isCarAvailable)
            {
                return BadRequest("Car is not available for the selected time range.");
            }
        
            string totalRentString = _itemService.CalculateTotalRent(bookcar);
            string pricePerHourString = car.pricePerHour.ToString();
            var value=0.0;
            if (double.TryParse(totalRentString, out double totalRent) &&
                double.TryParse(pricePerHourString, out double pricePerHour))
            {
                value = totalRent * pricePerHour;
            }
          



            var result = new BookedCar
            {
                Id = Guid.NewGuid(),
                CarId = carId,
                UserName = username,
                ModelName = car.Brand,
                To = bookcar.To,
                From = bookcar.From,
                Price = car.pricePerHour.ToString(),
                TotalRent = (long)value,
            };
            _bookcar.Insert(result);
            _bookcar.Save();
            return Ok(new { message = "Car booked successfully" });

        }

        //[Authorize(Roles = "User")]
        [HttpGet("{username}/MyBookedCars")]
        public async Task<ActionResult<BookedCar>> GetMyBookedCars(string username)
        {
            var allBookedCars = await _bookcar.GetAll();
            var myBookedCars = allBookedCars.Where(bookedCar => bookedCar.UserName == username).ToList();

            return Ok(myBookedCars);

        }
        //[Authorize(Roles = "User")]
        [HttpGet("CalculateTotalRent/{username}")]
        public async Task<ActionResult<UserTotalRentDto>> CalculateTotalRent(string username)
        {
            // Check if the user with the given username exists in the database
            var existingUserTotalRent = _context.userTotalRent.FirstOrDefault(x => x.UserName == username);

            var bookedCars = await _bookcar.GetAll();
            var userBookedCars = bookedCars.Where(x => x.UserName == username).ToList();

            var totalRent = userBookedCars.Sum(x => x.TotalRent);

            var productService = new ProductService();
            var productOptions = new ProductCreateOptions
            {
                Name = "Car Rental",
                Description = "Enjoy your Ride",
            };
            var product = productService.Create(productOptions);
            string productId = product.Id;

            var priceService = new PriceService();
            var priceOptions = new PriceCreateOptions
            {
                Product = productId,
                UnitAmount = totalRent * 100,
                Currency = "inr",
            };
            var price = priceService.Create(priceOptions);
            string priceId = price.Id;

            if (existingUserTotalRent != null)
            {
                // If the user already exists, update their total rent
                existingUserTotalRent.TotalRent = totalRent.ToString();
                existingUserTotalRent._StripePriceID = priceId;
                existingUserTotalRent._StripeProductId = productId;
            }
            else
            {
                // If the user doesn't exist, create a new entry
                var response = new UserTotalRentDto
                {
                    id = Guid.NewGuid(),
                    UserName = username,
                    TotalRent = totalRent.ToString(),
                    _StripePriceID = priceId,
                    _StripeProductId = productId
                };
                _userTotalRent.Insert(response);
            }

            _userTotalRent.Save();
            return Ok("Data calculated successfully");
        }


        //[Authorize(Roles = "User")]
        [HttpDelete]
        [Route("{id:guid}")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            var GetTheId = await _bookcar.GetById(id);
            if (GetTheId != null)
            {
                await _bookcar.Delete(id);
                return Ok();
            }
            return BadRequest();
        }
        //[Authorize(Roles = "Admin,User")]

        [HttpGet("TotalRent")]

        public async Task<ActionResult<List<UserTotalRentDto>>> TotalRent ()
        {
            var result = await _userTotalRent.GetAll();
            return Ok(result);
        }

        [HttpGet("check-customer")]
        public async Task<IActionResult> CheckCustomer(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return BadRequest("Email address is required.");
            }

            var customerService = new CustomerService();
            var existingCustomer = customerService.List(
                new CustomerListOptions { Email = email }
            ).FirstOrDefault();

            if (existingCustomer != null)
            {
                var customerId = existingCustomer.Id;
                return Ok("Yes exist with" + customerId);


        }
        
            else
            {
                return BadRequest("800");
            }
        }


        [Route("api/create-payment-intent")]
        [HttpPost]
        public async Task<IActionResult> CreatePaymentIntent(string PriceId)
        {

            var result = await _context.userTotalRent.FirstOrDefaultAsync(x => x._StripePriceID == PriceId);
            string amount = result.TotalRent;
            string currency = "inr";

            var options = new PaymentIntentCreateOptions
            {
                Amount = Convert.ToInt64(amount),
                Currency = currency,
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true,
                },
            };

            var service = new PaymentIntentService();
            var paymentIntent = service.Create(options);

            return Ok(new { PaymentIntentId = paymentIntent.Id });
        }

        [HttpPost("ApplyCoupon")]
        public async Task<ActionResult> ApplyCoupon([FromBody] ApplyCouponModel model)
        {
            try
            {
                var couponService = new CouponService();
                var couponOptions = new CouponCreateOptions
                {
                    PercentOff = (decimal?)25.5, 
                    Duration = "repeating", 
                    DurationInMonths = 3, 
                };

                var coupon = await couponService.CreateAsync(couponOptions);
                  return Ok(new { message = "Coupon applied successfully", couponId = coupon.Id });
            }
            catch (StripeException e)
            {
                return BadRequest(new { message = e.Message });
            }
        }




        [HttpGet("CheckOut-session")]
        public async Task<IActionResult> GetUrl(string email, string PriceId, string paymentIntentId, string couponCode, CancellationToken cancellationToken)
        {
            var result = await _context.userTotalRent.FirstOrDefaultAsync(x => x._StripePriceID == PriceId);
            if (string.IsNullOrEmpty(email))
            {
                return BadRequest("Email address is required.");
            }
            var customerService = new CustomerService();
            var existingCustomer = customerService.List(new CustomerListOptions { Email = email }).FirstOrDefault();

            if (existingCustomer != null)
            {
                var customerId = existingCustomer.Id;
                var couponService = new CouponService();
                var allCoupons = await couponService.ListAsync();
                var matchingCoupon = allCoupons.FirstOrDefault(coupon => coupon.Id == couponCode);

                if (matchingCoupon != null)
                {
                        var sessionOptions = new SessionCreateOptions
                        {
                           Customer = customerId,
                           PaymentMethodTypes = new List<string> { "card" },
                           LineItems = new List<SessionLineItemOptions>
                           {
                                new SessionLineItemOptions
                                {
                                   Price = PriceId,
                                   Quantity = 1,
                                },
                           },
                              Mode = "payment",
                              SuccessUrl = "https://your-website.com/success",
                               CancelUrl = "https://your-website.com/cancel",
                        };

                    // Apply the coupon to the session
                         sessionOptions.Discounts = new List<SessionDiscountOptions>
                         {
                             new SessionDiscountOptions
                             {
                                   Coupon = matchingCoupon.Id,
                             },
                         };

                    var sessionService = new SessionService();
                    var session = sessionService.Create(sessionOptions);

                    return Ok(session);
                }
                else
                {
                    return BadRequest("Invalid coupon code.");
                }
                }
                else
                {
                    return NotFound("Customer not found.");
                }
        }


        [HttpPost("customer")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<CustomerModel>> CreateCustomer([FromBody] CreateCustomerModel resource, CancellationToken cancellationToken)
        {
            var response = await _service.CreateCustomer(resource, cancellationToken);
            return Ok(response);
        }

    }

}

