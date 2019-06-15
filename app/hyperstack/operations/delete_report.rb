class DeleteReport < Hyperstack::ServerOp
  param :key  
  step { Report.find_by_delete_key(key).destroy.then {} }
end
