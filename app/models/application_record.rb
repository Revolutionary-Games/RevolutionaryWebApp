# app/models/application_record.rb
# the presence of this file prevents rails migrations from recreating application_record.rb
# see https://github.com/rails/rails/issues/29407

# Why is this line here?
# require 'models/application_record.rb'

class ApplicationRecord < ActiveRecord::Base
  self.abstract_class = true
end
