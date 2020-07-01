# frozen_string_literal: true

require 'sidekiq/web'
require 'admin_constraint'

Sidekiq::Web.set :sessions, false
Sidekiq::Web.set :session_secret, Rails.application.credentials[:secret_key_base]

Rails.application.routes.draw do
  get 'login/failed', to: 'login#failed'
  post 'login', to: 'login#do_login'
  delete 'login', to: 'login#logout'
  post 'logout', to: 'login#logout'
  get 'login/sso_return', to: 'login#sso_return'
  get 'login/sso_return_community', to: 'login#sso_return_community'
  get 'login/patreon', to: 'login#sso_return_patreon'
  mount Hyperstack::Engine => '/hyperstack'
  mount GrapeSwaggerRails::Engine => '/swagger'
  mount API::Base, at: '/'
  # Prevent API leakage when it doesn't exist
  get 'api/', to: proc { [404, {}, ['']] }

  constraints AdminConstraint.new do
    mount Sidekiq::Web => '/admin/sidekiq'
  end

  # The format part is needed to work with urls like /files/image.jpg
  get '/(*other)', to: 'hyperstack#app', format: false, defaults: { format: 'html' }
  # For details on the DSL available within this file, see http://guides.rubyonrails.org/routing.html
end
