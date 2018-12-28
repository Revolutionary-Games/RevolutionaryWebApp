class UserSerializer < ActiveModel::Serializer

  attributes :id, :email, :name, :admin, :developer, :local, :created_at
end  
