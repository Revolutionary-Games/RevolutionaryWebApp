Rails.application.routes.draw do
  get 'login/failed', to: "login#failed"
  post 'login', to: "login#do_login"
  delete 'login', to: "login#logout"
  post 'logout', to: "login#logout"
  get 'login/sso_return', to: "login#sso_return"
  mount Hyperstack::Engine => '/hyperstack'
  mount GrapeSwaggerRails::Engine => '/swagger'
  mount API::Base, at: "/"
  # Prevent API leakage when it doesn't exist
  get 'api/', to: proc { [404, {}, ['']] }
  
  get '/(*other)', to: 'hyperstack#app'
  # For details on the DSL available within this file, see http://guides.rubyonrails.org/routing.html
end
