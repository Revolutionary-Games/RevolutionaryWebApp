# frozen_string_literal: true

source 'https://rubygems.org'
git_source(:github) { |repo| "https://github.com/#{repo}.git" }

ruby '2.6.6'

# Bundle edge Rails instead: gem 'rails', github: 'rails/rails'
gem 'rails', '~> 5.2.2'
# Use sqlite3 as the database for Active Record
gem 'sqlite3'
# Use Puma as the app server
gem 'puma', '~> 3.12'
# Use SCSS for stylesheets
# gem 'sass-rails', '~> 5.0'
gem 'sassc-rails', '~> 2.1', '>= 2.1.1'

# Sidekiq jobs
gem 'redis-namespace'
gem 'sidekiq'

# Use Uglifier as compressor for JavaScript assets
gem 'uglifier', '>= 1.3.0'
# See https://github.com/rails/execjs#readme for more supported runtimes
# gem 'mini_racer', platforms: :ruby

# Use CoffeeScript for .coffee assets and views
# gem 'coffee-rails', '~> 4.2'
# Turbolinks makes navigating your web application faster. Read more: https://github.com/turbolinks/turbolinks
gem 'turbolinks', '~> 5'
# Build JSON APIs with ease. Read more: https://github.com/rails/jbuilder
gem 'jbuilder', '~> 2.5'
# Use Redis adapter to run Action Cable in production
# gem 'redis', '~> 4.0'
# Use ActiveModel has_secure_password
gem 'bcrypt', '~> 3.1.7'

# Use ActiveStorage variant
# gem 'mini_magick', '~> 4.8'

# Use Capistrano for deployment
# gem 'capistrano-rails', group: :development

# Reduces boot times through caching; required in config/boot.rb
gem 'bootsnap', '~> 1.3', '>= 1.3.2', require: false

group :development, :test do
  # Call 'byebug' anywhere in the code to stop execution and get a debugger console
  gem 'byebug', platforms: %i[mri mingw x64_mingw]
end

group :development do
  # Access an interactive console on exception pages or by calling 'console'
  # anywhere in the code.
  gem 'listen', '>= 3.0.5', '< 3.2'
  gem 'web-console', '>= 3.3.0'
  # Spring speeds up development by keeping your application running in the background.
  # Read more: https://github.com/rails/spring
  gem 'spring'
  gem 'spring-watcher-listen', '~> 2.0.0'

  # Local mail testing
  # NOTE: install this locally separately. And run it separately

  gem 'foreman'

  # Debugging helping gems
  gem 'pry'
  gem 'pry-doc'

  # Unfortunately the opal version is keeping this very outdated from hyperstack
  # gem 'rubocop' #, '>= 0.63'
end

group :test do
  # Adds support for Capybara system testing and selenium driver
  gem 'capybara', '>= 2.15'
  gem 'webdrivers'

  # Hyperstack testing
  gem 'database_cleaner'
  gem 'hyper-spec' # , path: '../hyperstack/ruby/hyper-spec'
end

# Windows does not include zoneinfo files, so bundle the tzinfo-data gem
gem 'tzinfo-data', platforms: %i[mingw mswin x64_mingw jruby]

# gem 'rails-hyperstack', '~> 1.0.alpha1'
# more up to date
gem 'rails-hyperstack', github: 'hyperstack-org/hyperstack', branch: 'edge', glob: 'ruby/*/*.gemspec'

gem 'webpacker'

# Data store
gem 'pg'
gem 'redis'

# api
gem 'grape', '~> 1.2', '>= 1.2.2'
gem 'grape-active_model_serializers'
gem 'grape-swagger'
# the swagger UI component for viewing it nicely
gem 'grape-swagger-rails'
gem 'rack-cors', require: 'rack/cors'
# Can't use this as this is too old
# gem 'grape-attack', '>= 0.3.0'

# Proper session store
gem 'activerecord-session_store'

# discord webhooks
gem 'discordrb', '~> 3.2', '>= 3.2.1'

# LFS uploads to S3 compatible service
gem 'aws-sdk-s3', '~> 1'

# This is used for email cancel tokens
gem 'jwt'

# For delegating StackWalk to an external service
gem 'rest-client'

gem 'sha3'
